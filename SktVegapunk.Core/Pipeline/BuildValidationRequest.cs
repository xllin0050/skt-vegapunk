namespace SktVegapunk.Core.Pipeline;

public sealed record BuildValidationRequest(
    string TargetPath,
    string BuildConfiguration,
    bool RunTestsAfterBuild);
