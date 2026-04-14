namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 中可辨識的互動事件或導頁動作。
/// </summary>
public sealed record JspInteractionEvent(
    int Order,
    string Kind,
    string Trigger,
    string? Target,
    string? Value,
    string SourceSnippet);
