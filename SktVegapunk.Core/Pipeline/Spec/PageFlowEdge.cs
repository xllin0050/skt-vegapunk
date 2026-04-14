namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 頁面流程圖中的有向邊。
/// </summary>
public sealed record PageFlowEdge(
    string Source,
    string Kind,
    string Trigger,
    string Target,
    string Detail);
