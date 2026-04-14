namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 單一 outgoing request 的 payload 對應摘要。
/// </summary>
public sealed record PayloadMappingArtifact(
    string JspSource,
    string Kind,
    string HttpMethod,
    string Target,
    string PayloadExpression,
    IReadOnlyList<RequestPayloadField> Fields);
