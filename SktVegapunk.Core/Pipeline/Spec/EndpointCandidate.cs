namespace SktVegapunk.Core.Pipeline.Spec;

public enum EndpointStatus
{
    Resolved,
    Unresolved
}

public sealed record EndpointCandidate(
    string JspSource,
    string PbMethod,
    string SuggestedHttpMethod,
    string SuggestedRoute,
    EndpointStatus Status,
    string? StatusReason = null);
