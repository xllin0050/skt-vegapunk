namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// DDL 中 Trigger 的結構定義。
/// </summary>
public sealed record SchemaTriggerSpec(
    string TriggerName,
    string TableName,
    IReadOnlyList<string> Events,
    string Body);
