namespace SktVegapunk.Core.Pipeline;

public interface IMigrationOrchestrator
{
    Task<MigrationResult> RunAsync(MigrationRequest request, CancellationToken cancellationToken = default);
}
