﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Docfx.YamlSerialization.Helpers;
using Docfx.YamlSerialization.ObjectDescriptors;
using Docfx.YamlSerialization.ObjectGraphTraversalStrategies;
using Docfx.YamlSerialization.ObjectGraphVisitors;
using Docfx.YamlSerialization.TypeInspectors;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectFactories;
using YamlDotNet.Serialization.ObjectGraphVisitors;
using YamlDotNet.Serialization.TypeInspectors;
using YamlDotNet.Serialization.TypeResolvers;

namespace Docfx.YamlSerialization;

public class YamlSerializer
{
    internal IList<IYamlTypeConverter> Converters { get; set; }
    private readonly SerializationOptions _options;
    private readonly INamingConvention _namingConvention;
    private readonly ITypeResolver _typeResolver;
    private readonly ITypeInspector _typeDescriptor;
    private readonly IObjectFactory _objectFactory;
    private readonly Settings _settings;

    public YamlSerializer(SerializationOptions options = SerializationOptions.None, INamingConvention? namingConvention = null, Settings? settings = null)
    {
        _options = options;
        _namingConvention = namingConvention ?? NullNamingConvention.Instance;
        _settings = settings ?? new Settings();

        Converters = new List<IYamlTypeConverter>();
        foreach (IYamlTypeConverter yamlTypeConverter in YamlTypeConverters.BuiltInConverters)
        {
            Converters.Add(yamlTypeConverter);
        }

        _typeResolver = IsOptionSet(SerializationOptions.DefaultToStaticType)
            ? new StaticTypeResolver()
            : new DynamicTypeResolver();

        _typeDescriptor = CreateTypeInspector();
        _objectFactory = new DefaultObjectFactory();
    }

    private bool IsOptionSet(SerializationOptions option)
    {
        return (_options & option) != 0;
    }

    public void Serialize(TextWriter writer, object graph)
    {
        Serialize(new Emitter(writer), graph);
    }

    public void Serialize(IEmitter emitter, object graph)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        EmitDocument(emitter, new BetterObjectDescriptor(graph, graph != null ? graph.GetType() : typeof(object), typeof(object)));
    }

    private void EmitDocument(IEmitter emitter, IObjectDescriptor graph)
    {
        var traversalStrategy = CreateTraversalStrategy();
        var eventEmitter = CreateEventEmitter();
        var emittingVisitor = CreateEmittingVisitor(emitter, traversalStrategy, eventEmitter, graph);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());

        traversalStrategy.Traverse(graph, emittingVisitor, emitter, GetObjectSerializer(emitter));

        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());
    }

    private IObjectGraphVisitor<IEmitter> CreateEmittingVisitor(IEmitter emitter, IObjectGraphTraversalStrategy traversalStrategy, IEventEmitter eventEmitter, IObjectDescriptor graph)
    {
        IObjectGraphVisitor<IEmitter> emittingVisitor = new EmittingObjectGraphVisitor(eventEmitter);

        emittingVisitor = new CustomSerializationObjectGraphVisitor(emittingVisitor, Converters, GetObjectSerializer(emitter));

        void nestedObjectSerializer(object? v, Type? t = null) => SerializeValue(emitter, v, t);

        emittingVisitor = new CustomSerializationObjectGraphVisitor(emittingVisitor, Converters, nestedObjectSerializer);

        if (!IsOptionSet(SerializationOptions.DisableAliases))
        {
            var anchorAssigner = new AnchorAssigner(Converters);

            traversalStrategy.Traverse(graph, anchorAssigner, default, GetObjectSerializer(emitter));

            emittingVisitor = new AnchorAssigningObjectGraphVisitor(emittingVisitor, eventEmitter, anchorAssigner);
        }

        if (!IsOptionSet(SerializationOptions.EmitDefaults))
        {
            emittingVisitor = new ExclusiveObjectGraphVisitor(emittingVisitor);
        }

        return emittingVisitor;
    }

    public void SerializeValue(IEmitter emitter, object? value, Type? type)
    {
        var graph = type != null
            ? new BetterObjectDescriptor(value, type, type)
            : new BetterObjectDescriptor(value, value != null ? value.GetType() : typeof(object), typeof(object));

        var traversalStrategy = CreateTraversalStrategy();
        var emittingVisitor = CreateEmittingVisitor(
            emitter,
            traversalStrategy,
            CreateEventEmitter(),
            graph
        );

        traversalStrategy.Traverse(graph, emittingVisitor, emitter, GetObjectSerializer(emitter));
    }

    private ObjectSerializer GetObjectSerializer(IEmitter emitter)
    {
        return (object? v, Type? t = null) => SerializeValue(emitter, v, t);
    }

    private IEventEmitter CreateEventEmitter()
    {
        var writer = new WriterEventEmitter();

        if (IsOptionSet(SerializationOptions.JsonCompatible))
        {
            return new JsonEventEmitter(writer, YamlFormatter.Default, NullNamingConvention.Instance, _typeDescriptor);
        }
        else
        {
            return new TypeAssigningEventEmitter(
                writer,
                new ConcurrentDictionary<Type, TagName>(),
                quoteNecessaryStrings: false,
                quoteYaml1_1Strings: false,
                defaultScalarStyle: ScalarStyle.Any,
                formatter: YamlFormatter.Default,
                enumNamingConvention: NullNamingConvention.Instance,
                typeInspector: _typeDescriptor);
        }
    }

    private IObjectGraphTraversalStrategy CreateTraversalStrategy()
    {
        return IsOptionSet(SerializationOptions.Roundtrip)
            ? new RoundtripObjectGraphTraversalStrategy(Converters, this, _typeDescriptor, _typeResolver, 50, _namingConvention, _settings, _objectFactory)
            : new FullObjectGraphTraversalStrategy(this, _typeDescriptor, _typeResolver, 50, _namingConvention, _objectFactory);
    }

    private ITypeInspector CreateTypeInspector()
    {
        ITypeInspector typeDescriptor = new EmitTypeInspector(_typeResolver);
        if (IsOptionSet(SerializationOptions.Roundtrip))
            typeDescriptor = new ReadableAndWritablePropertiesTypeInspector(typeDescriptor);

        typeDescriptor = new NamingConventionTypeInspector(typeDescriptor, _namingConvention);
        typeDescriptor = new YamlAttributesTypeInspector(typeDescriptor);

        return typeDescriptor;
    }
}

