namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// Schema 提取的完整結果，包含所有資料表、Trigger 與索引。
/// </summary>
public sealed record SchemaArtifacts(
    IReadOnlyList<SchemaTableSpec> Tables,
    IReadOnlyList<SchemaTriggerSpec> Triggers,
    IReadOnlyList<SchemaIndexSpec> StandaloneIndexes);
