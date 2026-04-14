namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 未解析 endpoint 的根因分析結果。
/// </summary>
public sealed record UnresolvedEndpointFinding(
    string JspSource,
    string PbMethod,
    string RootCause,
    string Detail);
