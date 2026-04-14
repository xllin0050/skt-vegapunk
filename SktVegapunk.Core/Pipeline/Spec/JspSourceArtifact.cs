namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 保留 JSP 原始內容與已提取的摘要，供後續分析器重用。
/// </summary>
public sealed record JspSourceArtifact(
    string JspFileName,
    string Content,
    JspInvocation Invocation,
    JspPrototypeArtifact Prototype);
