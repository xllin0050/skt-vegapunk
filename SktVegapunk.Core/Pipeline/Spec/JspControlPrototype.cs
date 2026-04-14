namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 頁面中可辨識的控制項摘要。
/// </summary>
public sealed record JspControlPrototype(
    string TagName,
    string? Type,
    string? Id,
    string? Name,
    string? Value,
    string? Text,
    string? FormKey,
    string? OnClickHandler);
