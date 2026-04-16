using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SchemaReconciliationAnalyzerTests
{
    private static readonly SchemaReconciliationAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_Schema中不存在的資料表應標示TableExistsInSchema為false()
    {
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "test.srd",
                Columns: [new SrdColumn("sign_kind", "s99_ghost_table.sign_kind", "char", 5)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["s99_ghost_table"])
        };
        var result = _analyzer.Analyze(srdSpecs, []);

        var entry = Assert.Single(result);
        Assert.Equal("s99_ghost_table", entry.TableName);
        Assert.False(entry.TableExistsInSchema);
    }

    [Fact]
    public void Analyze_兩端都有的欄位應不在差異列表中()
    {
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "test.srd",
                Columns: [new SrdColumn("sign_kind", "s99_sign_kind.sign_kind", "char", 5)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["s99_sign_kind"])
        };
        var schemaTables = new List<SchemaTableSpec>
        {
            new SchemaTableSpec(
                TableName: "s99_sign_kind",
                Columns: [new SchemaColumnSpec("sign_kind", "varchar(5)", false, null, null)],
                PrimaryKey: ["sign_kind"],
                ForeignKeys: [],
                Indexes: [],
                CheckConstraints: [])
        };

        var result = _analyzer.Analyze(srdSpecs, schemaTables);

        var entry = Assert.Single(result);
        Assert.True(entry.TableExistsInSchema);
        Assert.Empty(entry.ColumnsOnlyInSrd);
        Assert.Empty(entry.ColumnsOnlyInSchema);
    }

    [Fact]
    public void Analyze_Schema多的欄位應出現在ColumnsOnlyInSchema()
    {
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "test.srd",
                Columns: [new SrdColumn("sign_kind", "s99_sign_kind.sign_kind", "char", 5)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["s99_sign_kind"])
        };
        var schemaTables = new List<SchemaTableSpec>
        {
            new SchemaTableSpec(
                TableName: "s99_sign_kind",
                Columns:
                [
                    new SchemaColumnSpec("sign_kind", "varchar(5)", false, null, null),
                    new SchemaColumnSpec("upd_name", "varchar(18)", true, null, null)
                ],
                PrimaryKey: ["sign_kind"],
                ForeignKeys: [],
                Indexes: [],
                CheckConstraints: [])
        };

        var result = _analyzer.Analyze(srdSpecs, schemaTables);

        var entry = Assert.Single(result);
        Assert.Contains("upd_name", entry.ColumnsOnlyInSchema);
    }

    [Fact]
    public void Analyze_同張表被多個SrdSpec引用時欄位應累加()
    {
        // 兩個 DataWindow 各引用 s90_unitb 的不同欄位，分析器應累加而非只取第一筆
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "dw1.srd",
                Columns: [new SrdColumn("unt_id", "s90_unitb.unt_id", "char", 12)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["s90_unitb"]),
            new SrdSpec(
                FileName: "dw2.srd",
                Columns: [new SrdColumn("unt_name_full", "s90_unitb.unt_name_full", "char", 90)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["s90_unitb"])
        };
        var schemaTables = new List<SchemaTableSpec>
        {
            new SchemaTableSpec(
                TableName: "s90_unitb",
                Columns:
                [
                    new SchemaColumnSpec("unt_id", "char(12)", false, null, null),
                    new SchemaColumnSpec("unt_name_full", "varchar(90)", true, null, null),
                    new SchemaColumnSpec("unt_use_yn", "char(1)", true, null, null)
                ],
                PrimaryKey: ["unt_id"],
                ForeignKeys: [],
                Indexes: [],
                CheckConstraints: [])
        };

        var result = _analyzer.Analyze(srdSpecs, schemaTables);
        var entry = Assert.Single(result);
        Assert.Empty(entry.ColumnsOnlyInSrd);
        // 兩個 DataWindow 涵蓋 unt_id 與 unt_name_full，僅剩 unt_use_yn 未被引用
        Assert.Single(entry.ColumnsOnlyInSchema);
        Assert.Contains("unt_use_yn", entry.ColumnsOnlyInSchema);
    }

    [Fact]
    public void Analyze_PowerBuilder型別應與Sybase同義型別視為相同()
    {
        // string ↔ varchar、number ↔ decimal、long ↔ smallint 皆應視為等價
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "test.srd",
                Columns:
                [
                    new SrdColumn("c1", "t.c1", "string", 10),
                    new SrdColumn("c2", "t.c2", "number", null),
                    new SrdColumn("c3", "t.c3", "long", null)
                ],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["t"])
        };
        var schemaTables = new List<SchemaTableSpec>
        {
            new SchemaTableSpec(
                TableName: "t",
                Columns:
                [
                    new SchemaColumnSpec("c1", "varchar(10)", true, null, null),
                    new SchemaColumnSpec("c2", "decimal(18,2)", true, null, null),
                    new SchemaColumnSpec("c3", "smallint", true, null, null)
                ],
                PrimaryKey: [],
                ForeignKeys: [],
                Indexes: [],
                CheckConstraints: [])
        };

        var result = _analyzer.Analyze(srdSpecs, schemaTables);
        var entry = Assert.Single(result);
        Assert.Empty(entry.TypeMismatches);
    }

    [Fact]
    public void Analyze_不同類別型別應回報為不一致()
    {
        // long vs varchar 分屬整數/字串類別，應回報
        var srdSpecs = new List<SrdSpec>
        {
            new SrdSpec(
                FileName: "test.srd",
                Columns: [new SrdColumn("col", "t.col", "long", null)],
                RetrieveSql: string.Empty,
                Arguments: [],
                Tables: ["t"])
        };
        var schemaTables = new List<SchemaTableSpec>
        {
            new SchemaTableSpec(
                TableName: "t",
                Columns: [new SchemaColumnSpec("col", "varchar(5)", true, null, null)],
                PrimaryKey: [],
                ForeignKeys: [],
                Indexes: [],
                CheckConstraints: [])
        };

        var result = _analyzer.Analyze(srdSpecs, schemaTables);
        var entry = Assert.Single(result);
        var mismatch = Assert.Single(entry.TypeMismatches);
        Assert.Equal("col", mismatch.ColumnName);
    }

    [Fact]
    public void GenerateMarkdown_缺口資料表應在輸出中標示()
    {
        var entries = new List<SchemaReconciliationEntry>
        {
            new SchemaReconciliationEntry(
                TableName: "ghost_table",
                ColumnsOnlyInSrd: ["col_a"],
                ColumnsOnlyInSchema: [],
                TypeMismatches: [],
                SchemaPrimaryKey: [],
                TableExistsInSchema: false)
        };

        var markdown = _analyzer.GenerateMarkdown(entries);

        Assert.Contains("ghost_table", markdown);
        Assert.Contains("Schema 中不存在", markdown);
    }
}
