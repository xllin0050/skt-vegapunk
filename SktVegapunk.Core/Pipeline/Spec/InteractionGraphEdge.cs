namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 單一 click handler 到實際動作的互動邊。
/// </summary>
public sealed record InteractionGraphEdge(
    string JspFileName,
    string ClickTarget,
    string Handler,
    string ActionKind,
    string ActionTarget,
    string Detail);
