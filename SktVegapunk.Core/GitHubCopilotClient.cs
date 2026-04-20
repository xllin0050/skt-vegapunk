using GitHub.Copilot.SDK;
using System.Runtime.InteropServices;

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
        : this(new GitHubCopilotSdkExecutor(CreateOptions(githubToken, cliPath, workingDirectory)))
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
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        return _executor.ExecuteAsync(model, systemPrompt, userPrompt, timeout, cancellationToken);
    }

    public ValueTask DisposeAsync() => _executor.DisposeAsync();

    internal static CopilotClientOptions CreateOptions(
        string? githubToken,
        string? cliPath,
        string? workingDirectory)
    {
        return new CopilotClientOptions
        {
            CliPath = string.IsNullOrWhiteSpace(cliPath) ? null : cliPath,
            Cwd = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            GitHubToken = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken,
            Environment = CreateCliEnvironment(cliPath)
        };
    }

    internal static Dictionary<string, string>? CreateCliEnvironment(string? cliPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var platformCacheDirectory = Path.Combine(
            ResolveLinuxCacheRoot(),
            "copilot",
            "pkg",
            GetLinuxCliPlatformDirectoryName());

        // 先補齊 CLI 解壓縮時假設存在的父目錄；若家目錄 cache 不可寫，再退回 /tmp
        if (TryEnsureDirectory(platformCacheDirectory))
        {
            return null;
        }

        var fallbackCacheRoot = Path.Combine(Path.GetTempPath(), "skt-vegapunk", "copilot-cache");
        var fallbackPlatformDirectory = Path.Combine(
            fallbackCacheRoot,
            "copilot",
            "pkg",
            GetLinuxCliPlatformDirectoryName());

        if (!TryEnsureDirectory(fallbackPlatformDirectory))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["XDG_CACHE_HOME"] = fallbackCacheRoot
        };
    }

    private static string ResolveLinuxCacheRoot()
    {
        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCacheHome))
        {
            return xdgCacheHome;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".cache");
        }

        return Path.Combine(Path.GetTempPath(), "skt-vegapunk", "copilot-cache");
    }

    private static string GetLinuxCliPlatformDirectoryName()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "linux-arm64",
            _ => "linux-x64"
        };
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal interface IGitHubCopilotExecutor : IAsyncDisposable
{
    Task<string?> ExecuteAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        TimeSpan? timeout,
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
        TimeSpan? timeout,
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
        }, timeout, cancellationToken);

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
