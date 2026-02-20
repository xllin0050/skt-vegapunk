namespace SktVegapunk.Core.Pipeline;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default);
}
