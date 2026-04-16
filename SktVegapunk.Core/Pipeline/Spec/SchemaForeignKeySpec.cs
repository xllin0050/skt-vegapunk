namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中 Foreign Key 約束的結構定義。
/// </summary>
public sealed record SchemaForeignKeySpec(
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string? OnDelete);
