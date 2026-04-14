namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 單一 JSP 的控制項清單。
/// </summary>
public sealed record ControlInventoryArtifact(
    string JspSource,
    IReadOnlyList<JspControlPrototype> Controls);
