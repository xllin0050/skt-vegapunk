namespace SktVegapunk.Core.Pipeline;

/// <summary>
/// 使用 GitHub Copilot 生成 C# 內容。
/// </summary>
public sealed class CopilotCodeGenerator : ICodeGenerator
{
    private readonly GitHubCopilotClient _copilotClient;
    private readonly string _modelName;

    public CopilotCodeGenerator(GitHubCopilotClient copilotClient, string modelName)
    {
        ArgumentNullException.ThrowIfNull(copilotClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        _copilotClient = copilotClient;
        _modelName = modelName;
    }

    public async Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var content = await _copilotClient.SendMessageAsync(
            _modelName,
            systemPrompt,
            userPrompt,
            timeout: null,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("模型回傳空內容，無法繼續流程。");
        }

        return content;
    }
}
