namespace SktVegapunk.Core.Pipeline;

public sealed record MigrationRequest
{
    public required string SourceFilePath { get; init; }

    public required string OutputFilePath { get; init; }

    public required string TargetPath { get; init; }

    public required string SystemPrompt { get; init; }

    public int MaxRetries { get; init; } = 3;

    public bool RunTestsAfterBuild { get; init; }

    public string BuildConfiguration { get; init; } = "Debug";
}
