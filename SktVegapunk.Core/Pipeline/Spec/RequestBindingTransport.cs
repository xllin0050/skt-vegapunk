namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 描述 JSP 發出的表單或 Ajax 請求。
/// </summary>
public sealed record RequestBindingTransport(
    string Kind,
    string Target,
    string HttpMethod,
    string PayloadExpression,
    IReadOnlyList<RequestPayloadField> PayloadFields);
