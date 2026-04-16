namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中單一欄位的結構定義。
/// </summary>
public sealed record SchemaColumnSpec(
    string Name,
    string Type,
    bool Nullable,
    string? DefaultValue,
    string? Comment);
