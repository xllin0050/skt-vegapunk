namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record MigrationSpec(
    IReadOnlyList<SrdSpec> DataWindows,
    IReadOnlyList<SruSpec> Components,
    IReadOnlyList<JspInvocation> JspInvocations,
    IReadOnlyList<EndpointCandidate> EndpointCandidates,
    IReadOnlyList<string> UnresolvedMethods);
