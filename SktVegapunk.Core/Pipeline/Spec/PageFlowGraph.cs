namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 頁面流程圖的聚合結果。
/// </summary>
public sealed record PageFlowGraph(
    IReadOnlyList<string> Pages,
    IReadOnlyList<PageFlowEdge> Edges);
