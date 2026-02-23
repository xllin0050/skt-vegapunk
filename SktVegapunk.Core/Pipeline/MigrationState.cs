namespace SktVegapunk.Core.Pipeline;

public enum MigrationState
{
    Normalizing,
    Analyzing,
    Preprocessing,
    Generating,
    Validating,
    Repairing,
    Completed,
    Failed
}
