using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

public sealed class SrdExtractor : ISrdExtractor
{
    private static readonly Regex _columnRegex = new(
        @"column=\(type=(?<type>\w+(?:\(\d+\))?)\s+(?:update=\w+\s+)?(?:updatewhereclause=\w+\s+)?(?:key=\w+\s+)?name=(?<name>\w+)\s+dbname=""(?<dbname>[^""]+)""\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _argumentItemRegex = new(
        @"\(\s*""(?<name>\w+)""\s*,\s*(?<type>\w+)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _typeWithLengthRegex = new(
        @"^(?<type>\w+)\((?<length>\d+)\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SrdSpec Extract(string normalizedText)
    {
        var columns = new List<SrdColumn>();
        var arguments = new List<SrdArgument>();
        var tables = new HashSet<string>();
        string? retrieveSql = null;

        // 解析 column
        foreach (Match match in _columnRegex.Matches(normalizedText))
        {
            var name = match.Groups["name"].Value;
            var dbName = match.Groups["dbname"].Value;
            var type = match.Groups["type"].Value;
            int? maxLength = null;

            // 解析 char(40) 類型中的長度
            var typeMatch = _typeWithLengthRegex.Match(type);
            if (typeMatch.Success)
            {
                type = typeMatch.Groups["type"].Value;
                maxLength = int.Parse(typeMatch.Groups["length"].Value);
            }

            columns.Add(new SrdColumn(name, dbName, type, maxLength));

            // 從 dbname 中提取資料表名
            var tableName = ExtractTableNameFromDbName(dbName);
            if (!string.IsNullOrEmpty(tableName))
            {
                tables.Add(tableName);
            }
        }

        // 解析 retrieve
        retrieveSql = ExtractQuotedAssignmentValue(normalizedText, "retrieve=");

        // 解析 arguments
        var argsContent = ExtractParenthesizedAssignmentValue(normalizedText, "arguments=");
        if (!string.IsNullOrWhiteSpace(argsContent))
        {
            foreach (Match argMatch in _argumentItemRegex.Matches(argsContent))
            {
                var argName = argMatch.Groups["name"].Value;
                var argType = argMatch.Groups["type"].Value;
                arguments.Add(new SrdArgument(argName, argType));
            }
        }

        return new SrdSpec(
            FileName: "", // 由外部設定
            Columns: columns,
            RetrieveSql: retrieveSql ?? string.Empty,
            Arguments: arguments,
            Tables: tables.ToList().AsReadOnly());
    }

    private static string ExtractTableNameFromDbName(string dbName)
    {
        // dbName 格式如 "s99_sign_kind.sign_kind"，提取 "s99_sign_kind"
        var dotIndex = dbName.IndexOf('.');
        if (dotIndex > 0)
        {
            return dbName.Substring(0, dotIndex);
        }
        return string.Empty;
    }

    private static string? ExtractQuotedAssignmentValue(string text, string assignmentName)
    {
        var assignmentIndex = text.IndexOf(assignmentName, StringComparison.OrdinalIgnoreCase);
        if (assignmentIndex < 0)
        {
            return null;
        }

        var quoteStart = text.IndexOf('"', assignmentIndex + assignmentName.Length);
        if (quoteStart < 0)
        {
            return null;
        }

        var buffer = new List<char>();
        for (var i = quoteStart + 1; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '~' && i + 1 < text.Length)
            {
                buffer.Add(current);
                buffer.Add(text[++i]);
                continue;
            }

            if (current == '"')
            {
                return new string(buffer.ToArray());
            }

            buffer.Add(current);
        }

        return new string(buffer.ToArray());
    }

    private static string ExtractParenthesizedAssignmentValue(string text, string assignmentName)
    {
        var assignmentIndex = text.IndexOf(assignmentName, StringComparison.OrdinalIgnoreCase);
        if (assignmentIndex < 0)
        {
            return string.Empty;
        }

        var openParen = text.IndexOf('(', assignmentIndex + assignmentName.Length);
        if (openParen < 0)
        {
            return string.Empty;
        }

        var depth = 1;
        var start = openParen + 1;
        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start);
                    }
                    break;
            }
        }

        return string.Empty;
    }
}
