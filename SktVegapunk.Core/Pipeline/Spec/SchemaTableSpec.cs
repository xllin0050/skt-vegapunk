namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中單一資料表的完整結構定義。
/// </summary>
public sealed record SchemaTableSpec(
    string TableName,
    IReadOnlyList<SchemaColumnSpec> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<SchemaForeignKeySpec> ForeignKeys,
    IReadOnlyList<SchemaIndexSpec> Indexes,
    IReadOnlyList<SchemaCheckConstraintSpec> CheckConstraints);
