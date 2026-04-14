namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 描述單一 outgoing request payload 欄位。
/// </summary>
public sealed record RequestPayloadField(
    string Name,
    string SourceKind,
    string SourceExpression,
    string? SourceControl);
