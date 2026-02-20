namespace SktVegapunk.Core.Pipeline;

public sealed record MigrationResult
{
    public required MigrationState FinalState { get; init; }

    public required int Attempts { get; init; }

    public required string LastPrompt { get; init; }

    public string? GeneratedCode { get; init; }

    public string? LastValidationOutput { get; init; }

    public string? FailureReason { get; init; }
}
