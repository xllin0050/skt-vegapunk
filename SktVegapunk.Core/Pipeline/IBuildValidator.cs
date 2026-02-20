namespace SktVegapunk.Core.Pipeline;

public interface IBuildValidator
{
    Task<BuildValidationResult> ValidateAsync(
        BuildValidationRequest request,
        CancellationToken cancellationToken = default);
}
