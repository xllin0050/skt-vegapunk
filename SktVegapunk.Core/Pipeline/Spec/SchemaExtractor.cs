using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 解析 Sybase ASE DDL 文字，提取資料表、Trigger、索引結構。
/// </summary>
public sealed class SchemaExtractor : ISchemaExtractor
{
    // DDL 段落標記：-- DDL for Table/Index/Trigger '...'
    private static readonly Regex _ddlSectionHeaderRegex = new(
        @"--\s*DDL\s+for\s+(?<kind>Table|Index|Trigger)\s+'[^']*'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CREATE TABLE <name> (
    private static readonly Regex _createTableRegex = new(
        @"create\s+table\s+(?:[\w.]+\.)?(?<name>\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CONSTRAINT <name> PRIMARY KEY [CLUSTERED|NONCLUSTERED] (<cols>)
    private static readonly Regex _primaryKeyRegex = new(
        @"CONSTRAINT\s+\w+\s+PRIMARY\s+KEY\s+(?:CLUSTERED|NONCLUSTERED)?\s*\((?<cols>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CHECK constraint: CONSTRAINT <name> CHECK (<expr>)
    private static readonly Regex _checkConstraintRegex = new(
        @"CONSTRAINT\s+(?<name>\w+)\s+CHECK\s*\((?<expr>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // FOREIGN KEY (<cols>) REFERENCES <table> (<cols>) [ON DELETE ...]
    private static readonly Regex _foreignKeyRegex = new(
        @"FOREIGN\s+KEY\s*\((?<cols>[^)]+)\)\s+REFERENCES\s+(?:[\w.]+\.)?(?<refTable>\w+)\s*\((?<refCols>[^)]+)\)(?:\s+ON\s+DELETE\s+(?<onDelete>\w+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CREATE [NONCLUSTERED|CLUSTERED] INDEX <name> ON <table>(<cols>)
    private static readonly Regex _createIndexRegex = new(
        @"create\s+(?<clustered>clustered|nonclustered)?\s*index\s+(?<name>\w+)\s+on\s+(?:[\w.]+\.)?(?<table>\w+)\s*\((?<cols>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // CREATE TRIGGER [<owner>.]<name> ON [<owner>.]<table> [FOR|AFTER] <events>（events 僅捕捉到行尾，避免消耗 AS 後的 body）
    private static readonly Regex _createTriggerRegex = new(
        @"create\s+TRIGGER\s+(?:[\w]+\.)?(?<name>\w+)\s+ON\s+(?:[\w]+\.)?(?<table>\w+)\s+(?:FOR|AFTER)\s+(?<events>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 欄位定義：<name> <type> [DEFAULT ...] [NOT NULL | NULL]
    private static readonly Regex _columnRegex = new(
        @"^\s{1,8}(?<name>[a-zA-Z_]\w*)\s+(?<type>\w+(?:\(\d+(?:,\d+)?\))?)\s*(?:DEFAULT\s+(?<default>'[^']*'|\d+(?:\.\d+)?))?\s*(?<nullable>not\s+null|null)?\s*(?:,\s*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SchemaArtifacts Extract(string ddlText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ddlText);

        // 正規化換行符
        var normalizedText = ddlText.Replace("\r\n", "\n").Replace("\r", "\n");

        var segments = SplitIntoDdlSegments(normalizedText);

        var tables = new Dictionary<string, SchemaTableSpec>(StringComparer.OrdinalIgnoreCase);
        var triggers = new List<SchemaTriggerSpec>();
        var standaloneIndexes = new List<SchemaIndexSpec>();

        // 先收集每個 table 的 index，之後 merge 回去
        var tableIndexes = new Dictionary<string, List<SchemaIndexSpec>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (kind, body) in segments)
        {
            switch (kind.ToUpperInvariant())
            {
                case "TABLE":
                    var table = ParseTable(body);
                    if (table is not null)
                    {
                        tables[table.TableName] = table;
                    }
                    break;

                case "INDEX":
                    var (index, targetTable) = ParseIndex(body);
                    if (index is not null && targetTable is not null)
                    {
                        if (!tableIndexes.TryGetValue(targetTable, out var list))
                        {
                            list = [];
                            tableIndexes[targetTable] = list;
                        }
                        list.Add(index);
                        standaloneIndexes.Add(index);
                    }
                    break;

                case "TRIGGER":
                    var trigger = ParseTrigger(body);
                    if (trigger is not null)
                    {
                        triggers.Add(trigger);
                    }
                    break;
            }
        }

        // 將 standalone index 合併到對應 table
        var mergedTables = tables.Values
            .Select(t =>
            {
                if (!tableIndexes.TryGetValue(t.TableName, out var idxList))
                {
                    return t;
                }
                var merged = t.Indexes.ToList();
                merged.AddRange(idxList);
                return t with { Indexes = merged.AsReadOnly() };
            })
            .OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SchemaArtifacts(
            Tables: mergedTables.AsReadOnly(),
            Triggers: triggers.AsReadOnly(),
            StandaloneIndexes: standaloneIndexes.AsReadOnly());
    }

    private static List<(string Kind, string Body)> SplitIntoDdlSegments(string text)
    {
        var segments = new List<(string, string)>();
        var matches = _ddlSectionHeaderRegex.Matches(text);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var kind = match.Groups["kind"].Value;
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var body = text[start..end];
            segments.Add((kind, body));
        }

        return segments;
    }

    private static SchemaTableSpec? ParseTable(string body)
    {
        var createMatch = _createTableRegex.Match(body);
        if (!createMatch.Success)
        {
            return null;
        }

        var tableName = createMatch.Groups["name"].Value;

        // 找到 CREATE TABLE 後的第一個 ( 到對應 )
        var parenStart = body.IndexOf('(', createMatch.Index + createMatch.Length - 1);
        if (parenStart < 0)
        {
            return null;
        }

        var tableBody = ExtractBalancedParentheses(body, parenStart);
        if (tableBody is null)
        {
            return null;
        }

        var columns = new List<SchemaColumnSpec>();
        var primaryKey = new List<string>();
        var foreignKeys = new List<SchemaForeignKeySpec>();
        var checkConstraints = new List<SchemaCheckConstraintSpec>();

        // PRIMARY KEY
        var pkMatch = _primaryKeyRegex.Match(tableBody);
        if (pkMatch.Success)
        {
            primaryKey.AddRange(SplitColumns(pkMatch.Groups["cols"].Value));
        }

        // CHECK constraints
        foreach (Match chkMatch in _checkConstraintRegex.Matches(tableBody))
        {
            checkConstraints.Add(new SchemaCheckConstraintSpec(
                chkMatch.Groups["name"].Value,
                chkMatch.Groups["expr"].Value.Trim()));
        }

        // FOREIGN KEY constraints
        foreach (Match fkMatch in _foreignKeyRegex.Matches(tableBody))
        {
            foreignKeys.Add(new SchemaForeignKeySpec(
                Columns: SplitColumns(fkMatch.Groups["cols"].Value),
                ReferencedTable: fkMatch.Groups["refTable"].Value,
                ReferencedColumns: SplitColumns(fkMatch.Groups["refCols"].Value),
                OnDelete: fkMatch.Groups["onDelete"].Success ? fkMatch.Groups["onDelete"].Value : null));
        }

        // 欄位定義（逐行解析，跳過 constraint 行）
        var lines = tableBody.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 跳過空行、CONSTRAINT 行、LOCK 行
            if (string.IsNullOrWhiteSpace(trimmed)
                || trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var col = ParseColumn(line);
            if (col is not null)
            {
                columns.Add(col);
            }
        }

        return new SchemaTableSpec(
            TableName: tableName,
            Columns: columns.AsReadOnly(),
            PrimaryKey: primaryKey.AsReadOnly(),
            ForeignKeys: foreignKeys.AsReadOnly(),
            Indexes: new List<SchemaIndexSpec>().AsReadOnly(),
            CheckConstraints: checkConstraints.AsReadOnly());
    }

    private static SchemaColumnSpec? ParseColumn(string line)
    {
        var match = _columnRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value;
        var type = match.Groups["type"].Value;
        var nullableText = match.Groups["nullable"].Value;
        var defaultGroup = match.Groups["default"];

        // nullable 群組不存在代表未指定（Sybase 預設 nullable），明確 NOT NULL 則為 false
        var nullable = !nullableText.StartsWith("not", StringComparison.OrdinalIgnoreCase);
        var defaultValue = defaultGroup.Success ? defaultGroup.Value.Trim('\'') : null;

        return new SchemaColumnSpec(name, type, nullable, defaultValue, null);
    }

    private static (SchemaIndexSpec? Index, string? TargetTable) ParseIndex(string body)
    {
        var match = _createIndexRegex.Match(body);
        if (!match.Success)
        {
            return (null, null);
        }

        var clusteredText = match.Groups["clustered"].Value;
        var name = match.Groups["name"].Value;
        var table = match.Groups["table"].Value;
        var cols = SplitColumns(match.Groups["cols"].Value);

        var index = new SchemaIndexSpec(
            Name: name,
            Columns: cols,
            Unique: false,
            Clustered: clusteredText.Equals("clustered", StringComparison.OrdinalIgnoreCase));

        return (index, table);
    }

    private static SchemaTriggerSpec? ParseTrigger(string body)
    {
        var match = _createTriggerRegex.Match(body);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value;
        var table = match.Groups["table"].Value;
        var eventsText = match.Groups["events"].Value;

        // 每個逗號分隔段可能含有後續文字（如 "\nAS BEGIN..."），只取第一個 word
        var events = eventsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? string.Empty)
            .Where(e => e is "INSERT" or "UPDATE" or "DELETE")
            .ToList();

        // trigger body 從 AS 後開始，直到 setuser 或下一個 go 結束
        var bodyStart = match.Index + match.Length;
        var triggerBody = ExtractTriggerBody(body, bodyStart);

        return new SchemaTriggerSpec(
            TriggerName: name,
            TableName: table,
            Events: events.AsReadOnly(),
            Body: triggerBody.Trim());
    }

    private static string ExtractTriggerBody(string text, int startIndex)
    {
        // 找到 AS 關鍵字（在 FOR ... 後）
        var asIndex = text.IndexOf(" AS", startIndex, StringComparison.OrdinalIgnoreCase);
        if (asIndex < 0)
        {
            asIndex = text.IndexOf("\nAS", startIndex, StringComparison.OrdinalIgnoreCase);
        }

        var bodyStart = asIndex >= 0 ? asIndex : startIndex;

        // 找到 setuser 或結尾
        var setUserIndex = text.IndexOf("\nsetuser", bodyStart, StringComparison.OrdinalIgnoreCase);
        var endIndex = setUserIndex >= 0 ? setUserIndex : text.Length;

        return text[bodyStart..endIndex];
    }

    private static string? ExtractBalancedParentheses(string text, int openIndex)
    {
        if (openIndex >= text.Length || text[openIndex] != '(')
        {
            return null;
        }

        var depth = 1;
        var start = openIndex + 1;
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
                        return text[start..i];
                    }
                    break;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SplitColumns(string colText) =>
        colText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList()
            .AsReadOnly();
}
