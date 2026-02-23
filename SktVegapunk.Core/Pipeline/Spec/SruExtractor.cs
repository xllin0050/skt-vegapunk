using System.Text;
using System.Text.RegularExpressions;
using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Core.Pipeline.Spec;

public sealed class SruExtractor : ISruExtractor
{
    private static readonly Regex _typeDeclarationRegex = new(
        @"global\s+type\s+(?<className>\w+)\s+from\s+(?<parentClass>\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex _variableRegex = new(
        @"^(?<type>\w+)\s+(?<name>\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex _prototypeRegex = new(
        @"(?<accessLevel>public|private|protected)\s+(?<type>function|subroutine)\s+(?:(?<returnType>\w+|\w+\[\])\s+)?(?<name>\w+)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex _parameterRegex = new(
        @"(?:ref\s+)?(?<type>\w+|\w+\[\])\s+(?<name>\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _functionStartRegex = new(
        @"(?<accessLevel>public|private|protected)\s+(?<type>function|subroutine)\s+(?:(?<returnType>\w+|\w+\[\])\s+)?(?<name>\w+)\s*\((?<params>[^)]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex _functionEndRegex = new(
        @"end\s+(?<type>function|subroutine)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex _dataWindowReferenceRegex = new(
        @"(?:datawindow\s*=\s*""?(\w+)""?|\.\s*retrieve\s*\(\s*""?(\w+)""?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _sqlReferenceRegex = new(
        @"(?:select\s+|insert\s+|update\s+|delete\s+|execute\s+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IPbScriptExtractor _pbScriptExtractor;

    public SruExtractor(IPbScriptExtractor pbScriptExtractor)
    {
        _pbScriptExtractor = pbScriptExtractor;
    }

    public SruSpec Extract(string normalizedText)
    {
        // 解析 class 聲明
        var className = string.Empty;
        var parentClass = string.Empty;
        var typeMatch = _typeDeclarationRegex.Match(normalizedText);
        if (typeMatch.Success)
        {
            className = typeMatch.Groups["className"].Value;
            parentClass = typeMatch.Groups["parentClass"].Value;
        }

        // 解析 instance variables
        var instanceVariables = new List<string>();
        var variablesSection = ExtractSection(normalizedText, "type variables", "end variables");
        if (!string.IsNullOrEmpty(variablesSection))
        {
            foreach (Match match in _variableRegex.Matches(variablesSection))
            {
                var varName = match.Groups["name"].Value;
                instanceVariables.Add(varName);
            }
        }

        // 解析 forward prototypes
        var prototypes = new List<SruPrototype>();
        var prototypesSection = ExtractSection(normalizedText, "forward prototypes", "end prototypes");
        if (!string.IsNullOrEmpty(prototypesSection))
        {
            foreach (Match match in _prototypeRegex.Matches(prototypesSection))
            {
                var accessLevel = match.Groups["accessLevel"].Value;
                var type = match.Groups["type"].Value.ToLower();
                var returnType = match.Groups["returnType"].Value;
                var name = match.Groups["name"].Value;
                var paramsText = match.Groups["params"].Value;

                var parameters = ParseParameters(paramsText);
                prototypes.Add(new SruPrototype(
                    accessLevel,
                    returnType,
                    name,
                    parameters,
                    type == "function"));
            }
        }

        // 解析 routines（函式本文）
        var routines = new List<SruRoutine>();
        var routinesScanText = RemoveSection(normalizedText, "forward prototypes", "end prototypes");
        var functionMatches = _functionStartRegex.Matches(routinesScanText).Cast<Match>();
        foreach (Match startMatch in functionMatches)
        {
            var startIndex = startMatch.Index;
            var accessLevel = startMatch.Groups["accessLevel"].Value;
            var type = startMatch.Groups["type"].Value.ToLower();
            var returnType = startMatch.Groups["returnType"].Value;
            var name = startMatch.Groups["name"].Value;
            var paramsText = startMatch.Groups["params"].Value;

            var parameters = ParseParameters(paramsText);
            var prototype = new SruPrototype(accessLevel, returnType, name, parameters, type == "function");

            // 找到函式結束位置
            var functionBody = new StringBuilder();
            var searchIndex = startIndex + startMatch.Length;
            var endMatch = _functionEndRegex.Match(routinesScanText, searchIndex);
            if (endMatch.Success && endMatch.Groups["type"].Value.ToLower() == type)
            {
                var bodyEndIndex = endMatch.Index;
                var bodyText = routinesScanText.Substring(searchIndex, bodyEndIndex - searchIndex).Trim();
                functionBody.AppendLine(bodyText);
            }

            var body = functionBody.ToString();
            var referencedDataWindows = ExtractDataWindowReferences(body);
            var referencedSql = ExtractSqlReferences(body);

            routines.Add(new SruRoutine(
                prototype,
                body,
                referencedDataWindows,
                referencedSql));
        }

        // 解析 event blocks
        var eventBlocks = _pbScriptExtractor.Extract(normalizedText);

        return new SruSpec(
            FileName: "", // 由外部設定
            ClassName: className,
            ParentClass: parentClass,
            InstanceVariables: instanceVariables,
            Prototypes: prototypes,
            Routines: routines,
            EventBlocks: eventBlocks);
    }

    private static string ExtractSection(string text, string startMarker, string endMarker)
    {
        var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0) return string.Empty;

        startIndex += startMarker.Length;
        var endIndex = text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0) return string.Empty;

        return text.Substring(startIndex, endIndex - startIndex);
    }

    private static string RemoveSection(string text, string startMarker, string endMarker)
    {
        var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return text;
        }

        var endIndex = text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0)
        {
            return text;
        }

        endIndex += endMarker.Length;
        return text.Remove(startIndex, endIndex - startIndex);
    }

    private static IReadOnlyList<SruParameter> ParseParameters(string paramsText)
    {
        var parameters = new List<SruParameter>();
        if (string.IsNullOrWhiteSpace(paramsText)) return parameters;

        var parts = paramsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var match = _parameterRegex.Match(part.Trim());
            if (match.Success)
            {
                var paramType = match.Groups["type"].Value;
                var paramName = match.Groups["name"].Value;
                parameters.Add(new SruParameter(paramName, paramType));
            }
        }

        return parameters;
    }

    private static IReadOnlyList<string> ExtractDataWindowReferences(string text)
    {
        var dataWindows = new HashSet<string>();
        foreach (Match match in _dataWindowReferenceRegex.Matches(text))
        {
            var group1 = match.Groups[1].Value;
            var group2 = match.Groups[2].Value;
            var dwName = !string.IsNullOrEmpty(group1) ? group1 : group2;
            if (!string.IsNullOrEmpty(dwName))
            {
                dataWindows.Add(dwName);
            }
        }
        return dataWindows.ToList().AsReadOnly();
    }

    private static IReadOnlyList<string> ExtractSqlReferences(string text)
    {
        var sqlStatements = new HashSet<string>();
        foreach (Match match in _sqlReferenceRegex.Matches(text))
        {
            sqlStatements.Add(match.Value);
        }
        return sqlStatements.ToList().AsReadOnly();
    }
}
