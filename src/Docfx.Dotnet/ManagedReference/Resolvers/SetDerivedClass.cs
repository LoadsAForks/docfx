﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Docfx.DataContracts.ManagedReference;

namespace Docfx.Dotnet;

internal class SetDerivedClass : IResolverPipeline
{
    private readonly Dictionary<string, List<string>> _derivedClassMapping = [];

    public void Run(MetadataModel yaml, ResolverContext context)
    {
        if (yaml.Members is { Count: > 0 })
        {
            UpdateDerivedClassMapping(yaml.Members, context.References);
            AppendDerivedClass(yaml.Members);
        }
    }

    private void UpdateDerivedClassMapping(List<MetadataItem> items, Dictionary<string, ReferenceItem> reference)
    {
        foreach (var item in items ?? Enumerable.Empty<MetadataItem>())
        {
            var inheritance = item.Inheritance;
            if (inheritance is { Count: > 0 })
            {
                var superClass = inheritance[inheritance.Count - 1];

                if (reference.TryGetValue(superClass, out ReferenceItem referenceItem))
                {
                    superClass = referenceItem.Definition ?? superClass;
                }

                // ignore System.Object's derived class
                if (superClass != "System.Object")
                {
                    if (_derivedClassMapping.TryGetValue(superClass, out List<string> derivedClasses))
                    {
                        derivedClasses.Add(item.Name);
                    }
                    else
                    {
                        _derivedClassMapping.Add(superClass, [item.Name]);
                    }
                }
            }
        }
    }

    private void AppendDerivedClass(List<MetadataItem> items)
    {
        foreach (var item in items ?? Enumerable.Empty<MetadataItem>())
        {
            if (item.Type == MemberType.Class)
            {
                if (_derivedClassMapping.TryGetValue(item.Name, out List<string> derivedClasses))
                {
                    derivedClasses.Sort();
                    item.DerivedClasses = derivedClasses;
                }
            }
        }
    }
}
