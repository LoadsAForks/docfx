// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Markdig.Helpers;

namespace Docfx.MarkdigEngine.Extensions;

public partial class CodeSnippetExtractor
{
    [GeneratedRegex(@"^[\w\.-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex TagnameFormat();

    private readonly string StartLineTemplate;
    private readonly string EndLineTemplate;
    private readonly bool IsEndLineContainsTagName;
    public const string TagNamePlaceHolder = "{tagname}";

    public CodeSnippetExtractor(string startLineTemplate, string endLineTemplate, bool isEndLineContainsTagName = true)
    {
        StartLineTemplate = startLineTemplate;
        EndLineTemplate = endLineTemplate;
        IsEndLineContainsTagName = isEndLineContainsTagName;
    }

    public Dictionary<string, CodeRange> GetAllTags(string[] lines, ref HashSet<int> tagLines)
    {
        var result = new Dictionary<string, CodeRange>(StringComparer.OrdinalIgnoreCase);
        var tagStack = new Stack<string>();

        for (int index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (MatchTag(line, EndLineTemplate, out var tagName, IsEndLineContainsTagName))
            {
                tagLines.Add(index);
                if (!IsEndLineContainsTagName)
                {
                    tagName = tagStack.Count > 0 ? tagStack.Pop() : string.Empty;
                }

                if (result.TryGetValue(tagName, out var range))
                {
                    if (range.End == 0)
                    {
                        // we meet the first end tag, ignore the following ones
                        range.End = index;
                    }
                }

                continue;
            }

            if (MatchTag(line, StartLineTemplate, out tagName))
            {
                tagLines.Add(index);
                result[tagName] = new CodeRange { Start = index + 2 };
                tagStack.Push(tagName);
            }

        }

        return result;
    }

    private static bool MatchTag(string line, string template, out string tagName, bool containTagName = true)
    {
        tagName = string.Empty;
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(template)) return false;

        var splittedTemplate = template.Split(TagNamePlaceHolder);
        var beforeTagName = splittedTemplate[0];
        var afterTagName = splittedTemplate.Length == 2 ? splittedTemplate[1] : string.Empty;

        int column = 0;
        int index = 0;

        // match before
        while (column < line.Length && index < beforeTagName.Length)
        {
            if (!CharHelper.IsWhitespace(line[column]))
            {
                if (char.ToLower(line[column]) != beforeTagName[index]) return false;
                index++;
            }
            column++;
        }

        if (index != beforeTagName.Length) return false;

        //match tagname
        var sb = new StringBuilder();
        while (column < line.Length && (afterTagName == string.Empty || line[column] != afterTagName[0]))
        {
            sb.Append(line[column]);
            column++;
        }
        tagName = sb.ToString().Trim().ToLower();

        //match after tagname
        index = 0;
        while (column < line.Length && index < afterTagName.Length)
        {
            if (!CharHelper.IsWhitespace(line[column]))
            {
                if (char.ToLower(line[column]) != afterTagName[index]) return false;
                index++;
            }
            column++;
        }

        if (index != afterTagName.Length) return false;
        while (column < line.Length && CharHelper.IsWhitespace(line[column])) column++;

        return column == line.Length && (!containTagName || TagnameFormat().IsMatch(tagName));
    }
}
