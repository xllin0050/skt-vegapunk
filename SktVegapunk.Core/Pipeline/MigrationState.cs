namespace SktVegapunk.Core.Pipeline;

public enum MigrationState
{
    Preprocessing,
    Generating,
    Validating,
    Repairing,
    Completed,
    Failed
}
