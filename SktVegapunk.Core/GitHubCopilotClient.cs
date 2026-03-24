using GitHub.Copilot.SDK;

namespace SktVegapunk.Core;

/// <summary>
/// 封裝 GitHub Copilot SDK，對外提供單次訊息生成能力。
/// </summary>
public sealed class GitHubCopilotClient : IAsyncDisposable
{
    private readonly IGitHubCopilotExecutor _executor;

    public GitHubCopilotClient(
        string? githubToken = null,
        string? cliPath = null,
        string? workingDirectory = null)
        : this(new GitHubCopilotSdkExecutor(new CopilotClientOptions
        {
            CliPath = string.IsNullOrWhiteSpace(cliPath) ? null : cliPath,
            Cwd = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            GitHubToken = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken
        }))
    {
    }

    internal GitHubCopilotClient(IGitHubCopilotExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        _executor = executor;
    }

    /// <summary>
    /// 送出系統提示詞與使用者提示詞，並回傳 Copilot 最終內容。
    /// </summary>
    public Task<string?> SendMessageAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        return _executor.ExecuteAsync(model, systemPrompt, userPrompt, cancellationToken);
    }

    public ValueTask DisposeAsync() => _executor.DisposeAsync();
}

internal interface IGitHubCopilotExecutor : IAsyncDisposable
{
    Task<string?> ExecuteAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}

internal sealed class GitHubCopilotSdkExecutor : IGitHubCopilotExecutor
{
    private readonly CopilotClient _client;
    private bool _started;

    public GitHubCopilotSdkExecutor(CopilotClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = new CopilotClient(options);
    }

    public async Task<string?> ExecuteAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemPrompt
            },
            OnPermissionRequest = PermissionHandler.ApproveAll
        });
        var response = await session.SendAndWaitAsync(new MessageOptions
        {
            Prompt = userPrompt
        }, cancellationToken: cancellationToken);

        return response?.Data.Content;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        // CLI server 啟動成本高，因此整個程式生命週期內只啟動一次
        await _client.StartAsync(cancellationToken);
        _started = true;
    }
}
