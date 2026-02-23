namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record JspInvocation(
    string JspFileName,
    string ComponentName,
    string MethodName,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> HttpParameters);
