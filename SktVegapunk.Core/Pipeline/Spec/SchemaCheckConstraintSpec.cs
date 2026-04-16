namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中 CHECK 約束的結構定義。
/// </summary>
public sealed record SchemaCheckConstraintSpec(
    string Name,
    string Expression);
