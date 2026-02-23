namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record SruPrototype(
    string AccessLevel,
    string? ReturnType,
    string Name,
    IReadOnlyList<SruParameter> Parameters,
    bool IsFunction);

public sealed record SruParameter(
    string Name,
    string Type);
