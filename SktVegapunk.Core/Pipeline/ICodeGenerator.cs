namespace SktVegapunk.Core.Pipeline;

public interface ICodeGenerator
{
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
