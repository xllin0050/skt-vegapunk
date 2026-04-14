namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 描述 JSP 入口參數如何對應到 PB component 方法與後續請求。
/// </summary>
public sealed record RequestBindingArtifact(
    string JspSource,
    string PbMethod,
    string SuggestedHttpMethod,
    string SuggestedRoute,
    string Status,
    IReadOnlyList<RequestBindingParameter> Parameters,
    IReadOnlyList<RequestBindingTransport> OutgoingRequests);
