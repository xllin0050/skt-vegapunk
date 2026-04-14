namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 互動事件鏈的平面圖。
/// </summary>
public sealed record InteractionGraph(
    IReadOnlyList<InteractionGraphEdge> Edges);
