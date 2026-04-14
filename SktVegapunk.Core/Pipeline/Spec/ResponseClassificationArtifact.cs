namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 描述 endpoint 的回應型態推斷結果。
/// </summary>
public sealed record ResponseClassificationArtifact(
    string JspSource,
    string PbMethod,
    string SuggestedHttpMethod,
    string SuggestedRoute,
    string ResponseKind,
    string Confidence,
    string Evidence);
