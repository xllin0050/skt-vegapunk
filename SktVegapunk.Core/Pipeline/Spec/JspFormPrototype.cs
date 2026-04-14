namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 表單的原型資訊。
/// </summary>
public sealed record JspFormPrototype(
    string? Id,
    string? Name,
    string? Method,
    string? Action,
    string? Target);
