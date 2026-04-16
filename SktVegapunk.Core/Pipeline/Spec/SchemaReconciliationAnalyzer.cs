using System.Text;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 比對 SrdSpec 與 SchemaTableSpec，產出欄位差異與 PK 驗證結果。
/// </summary>
public sealed class SchemaReconciliationAnalyzer
{
    public IReadOnlyList<SchemaReconciliationEntry> Analyze(
        IReadOnlyList<SrdSpec> srdSpecs,
        IReadOnlyList<SchemaTableSpec> schemaTables)
    {
        ArgumentNullException.ThrowIfNull(srdSpecs);
        ArgumentNullException.ThrowIfNull(schemaTables);

        var schemaByTable = schemaTables.ToDictionary(
            t => t.TableName,
            t => t,
            StringComparer.OrdinalIgnoreCase);

        // 先累加所有 SrdSpec 中對同一張表的欄位與型別引用，避免同表被多 DataWindow 引用時遺漏欄位
        var srdColumnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var srdTypeByTable = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var srd in srdSpecs)
        {
            foreach (var col in srd.Columns)
            {
                var dotIndex = col.DbName.IndexOf('.');
                if (dotIndex <= 0)
                {
                    continue;
                }

                var tableName = col.DbName[..dotIndex];
                var colName = col.DbName[(dotIndex + 1)..];

                if (!srdColumnsByTable.TryGetValue(tableName, out var colSet))
                {
                    colSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    srdColumnsByTable[tableName] = colSet;
                }
                colSet.Add(colName);

                if (!srdTypeByTable.TryGetValue(tableName, out var typeMap))
                {
                    typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    srdTypeByTable[tableName] = typeMap;
                }
                // 若同欄位被多處引用，以第一個遇到的型別為準（同一欄位在 DataWindow 間型別通常一致）
                if (!typeMap.ContainsKey(colName))
                {
                    typeMap[colName] = col.Type;
                }
            }

            // 若 SrdSpec.Tables 標了某張表但欄位 DbName 沒帶前綴（例如 join 來源），至少保留表名鍵
            foreach (var tableName in srd.Tables)
            {
                if (!srdColumnsByTable.ContainsKey(tableName))
                {
                    srdColumnsByTable[tableName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        var tableResults = new Dictionary<string, SchemaReconciliationEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tableName, srdCols) in srdColumnsByTable)
        {
            if (!schemaByTable.TryGetValue(tableName, out var schemaTable))
            {
                tableResults[tableName] = new SchemaReconciliationEntry(
                    TableName: tableName,
                    ColumnsOnlyInSrd: srdCols.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(),
                    ColumnsOnlyInSchema: [],
                    TypeMismatches: [],
                    SchemaPrimaryKey: [],
                    TableExistsInSchema: false);
                continue;
            }

            var schemaCols = schemaTable.Columns
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var onlyInSrd = srdCols.Except(schemaCols, StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyInSchema = schemaCols.Except(srdCols, StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

            // 型別比對：只比對兩邊都有的欄位
            var typeMismatches = new List<SchemaColumnTypeMismatch>();
            var schemaColMap = schemaTable.Columns.ToDictionary(
                c => c.Name,
                c => c,
                StringComparer.OrdinalIgnoreCase);
            var srdTypes = srdTypeByTable.TryGetValue(tableName, out var tmap)
                ? tmap
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var colName in srdCols.Intersect(schemaCols, StringComparer.OrdinalIgnoreCase))
            {
                if (!srdTypes.TryGetValue(colName, out var srdType) || !schemaColMap.TryGetValue(colName, out var schemaCol))
                {
                    continue;
                }

                var srdBaseType = srdType.ToUpperInvariant();
                var schemaBaseType = StripLength(schemaCol.Type).ToUpperInvariant();

                if (!AreSameBaseType(srdBaseType, schemaBaseType))
                {
                    typeMismatches.Add(new SchemaColumnTypeMismatch(colName, srdType, schemaCol.Type));
                }
            }

            var schemaPk = schemaTable.PrimaryKey.ToList().AsReadOnly();

            tableResults[tableName] = new SchemaReconciliationEntry(
                TableName: tableName,
                ColumnsOnlyInSrd: onlyInSrd.AsReadOnly(),
                ColumnsOnlyInSchema: onlyInSchema.AsReadOnly(),
                TypeMismatches: typeMismatches.AsReadOnly(),
                SchemaPrimaryKey: schemaPk,
                TableExistsInSchema: true);
        }

        return tableResults.Values
            .OrderBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public string GenerateMarkdown(IReadOnlyList<SchemaReconciliationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        builder.AppendLine("# Schema Reconciliation");
        builder.AppendLine();
        builder.AppendLine("比對 SrdSpec（DataWindow 推測）與 Schema DDL 的差異。");
        builder.AppendLine();

        var missingTables = entries.Where(e => !e.TableExistsInSchema).ToList();
        var matchedTables = entries.Where(e => e.TableExistsInSchema).ToList();

        builder.AppendLine($"- 總計 SrdSpec 引用資料表：{entries.Count}");
        builder.AppendLine($"- Schema 中存在：{matchedTables.Count}");
        builder.AppendLine($"- Schema 中不存在（可能為 view 或 alias）：{missingTables.Count}");
        builder.AppendLine();

        if (missingTables.Count > 0)
        {
            builder.AppendLine("## Schema 中不存在的資料表");
            builder.AppendLine();
            foreach (var entry in missingTables)
            {
                builder.AppendLine($"- `{entry.TableName}`（SrdSpec 欄位: {string.Join(", ", entry.ColumnsOnlyInSrd.Take(5))}{(entry.ColumnsOnlyInSrd.Count > 5 ? "..." : "")}）");
            }
            builder.AppendLine();
        }

        foreach (var entry in matchedTables)
        {
            // 只有 SrdSpec 引用到 Schema 未定義的欄位或型別不一致才視為問題
            // （ColumnsOnlyInSchema 僅代表 DataWindow 未使用，不算缺口）
            var hasIssues = entry.ColumnsOnlyInSrd.Count > 0
                || entry.TypeMismatches.Count > 0;

            builder.AppendLine($"## {entry.TableName} {(hasIssues ? "⚠️" : "✅")}");
            builder.AppendLine();

            if (entry.SchemaPrimaryKey.Count > 0)
            {
                builder.AppendLine($"- **PK**：{string.Join(", ", entry.SchemaPrimaryKey)}");
            }

            if (entry.ColumnsOnlyInSrd.Count > 0)
            {
                builder.AppendLine($"- **SrdSpec 有、Schema 無**（可能為 view/computed）：{string.Join(", ", entry.ColumnsOnlyInSrd)}");
            }

            if (entry.ColumnsOnlyInSchema.Count > 0)
            {
                builder.AppendLine($"- **Schema 有、SrdSpec 無**（DataWindow 未使用，但 Entity 需要）：{string.Join(", ", entry.ColumnsOnlyInSchema)}");
            }

            if (entry.TypeMismatches.Count > 0)
            {
                builder.AppendLine("- **型別不一致**：");
                foreach (var mismatch in entry.TypeMismatches)
                {
                    builder.AppendLine($"  - `{mismatch.ColumnName}`：SrdSpec={mismatch.SrdType}, Schema={mismatch.SchemaType}");
                }
            }

            if (!hasIssues)
            {
                builder.AppendLine("- 無差異");
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string StripLength(string type)
    {
        var parenIndex = type.IndexOf('(');
        return parenIndex >= 0 ? type[..parenIndex] : type;
    }

    // PowerBuilder DataWindow 型別 → Sybase ASE DDL 型別的等價類別
    // 同一個類別內的型別視為等價，只要兩邊落在同一類別就不算型別不一致
    private static readonly Dictionary<string, string> _typeCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        // 字串類
        ["CHAR"] = "STRING",
        ["VARCHAR"] = "STRING",
        ["STRING"] = "STRING",
        ["NCHAR"] = "STRING",
        ["NVARCHAR"] = "STRING",
        ["TEXT"] = "STRING",

        // 整數類
        ["LONG"] = "INTEGER",
        ["INT"] = "INTEGER",
        ["INTEGER"] = "INTEGER",
        ["SMALLINT"] = "INTEGER",
        ["TINYINT"] = "INTEGER",
        ["BIGINT"] = "INTEGER",

        // 數值類（含可能帶小數的 decimal/numeric；PB 的 number 亦歸於此）
        ["NUMBER"] = "NUMERIC",
        ["NUMERIC"] = "NUMERIC",
        ["DECIMAL"] = "NUMERIC",
        ["DEC"] = "NUMERIC",
        ["REAL"] = "NUMERIC",
        ["FLOAT"] = "NUMERIC",
        ["DOUBLE"] = "NUMERIC",
        ["MONEY"] = "NUMERIC",
        ["SMALLMONEY"] = "NUMERIC",

        // 日期時間類
        ["DATE"] = "DATETIME",
        ["DATETIME"] = "DATETIME",
        ["SMALLDATETIME"] = "DATETIME",
        ["TIME"] = "DATETIME",
        ["TIMESTAMP"] = "DATETIME",
    };

    private static bool AreSameBaseType(string srdType, string schemaType)
    {
        if (string.Equals(srdType, schemaType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 未收錄型別回退為字串比對；已收錄型別則以類別比對
        var srdHasCategory = _typeCategory.TryGetValue(srdType, out var srdCategory);
        var schemaHasCategory = _typeCategory.TryGetValue(schemaType, out var schemaCategory);

        if (srdHasCategory && schemaHasCategory)
        {
            return string.Equals(srdCategory, schemaCategory, StringComparison.Ordinal);
        }

        return false;
    }
}
