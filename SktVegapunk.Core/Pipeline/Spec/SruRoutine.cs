namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record SruRoutine(
    SruPrototype Prototype,
    string Body,
    IReadOnlyList<string> ReferencedDataWindows,
    IReadOnlyList<string> ReferencedSql);
