namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// LLM 從 JSP + DB schema 推導出的 endpoint 規格。
/// </summary>
public sealed record InferredEndpointSpec(
    string JspSource,
    string PbMethod,
    string SuggestedHttpMethod,
    string SuggestedRoute,
    string BusinessSummary,
    IReadOnlyList<string> InputParameters,
    IReadOnlyList<string> RelatedTables,
    string ResponseType,
    bool InferenceSucceeded);
