namespace SktVegapunk.Core.Pipeline;

public sealed record SourceArtifact(
    string OriginalPath,
    string NormalizedText,
    string SourceEncoding,
    IReadOnlyList<string> Warnings);
