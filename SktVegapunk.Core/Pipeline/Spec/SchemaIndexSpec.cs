namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中索引的結構定義。
/// </summary>
public sealed record SchemaIndexSpec(
    string Name,
    IReadOnlyList<string> Columns,
    bool Unique,
    bool Clustered);
