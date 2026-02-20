namespace SktVegapunk.Core.Pipeline;

public sealed class OpenRouterCodeGenerator : ICodeGenerator
{
    private readonly OpenRouterClient _openRouterClient;
    private readonly string _modelName;

    public OpenRouterCodeGenerator(OpenRouterClient openRouterClient, string modelName)
    {
        ArgumentNullException.ThrowIfNull(openRouterClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        _openRouterClient = openRouterClient;
        _modelName = modelName;
    }

    public async Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var content = await _openRouterClient.SendMessageAsync(
            _modelName,
            systemPrompt,
            userPrompt,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("模型回傳空內容，無法繼續流程。");
        }

        return content;
    }
}
