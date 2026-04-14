namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// JSP 頁面的 HTML、JavaScript、CSS 原型與摘要資訊。
/// </summary>
public sealed record JspPrototypeArtifact(
    string JspFileName,
    string HtmlPrototype,
    string JavaScriptPrototype,
    string CssPrototype,
    IReadOnlyList<JspFormPrototype> Forms,
    IReadOnlyList<JspControlPrototype> Controls,
    IReadOnlyList<JspInteractionEvent> Events,
    IReadOnlyList<string> ScriptSources,
    IReadOnlyList<string> StyleSources,
    IReadOnlyList<string> HttpParameters,
    string ComponentName,
    string MethodName);
