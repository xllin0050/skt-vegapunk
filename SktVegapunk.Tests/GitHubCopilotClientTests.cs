using SktVegapunk.Core;
using System.Runtime.InteropServices;

namespace SktVegapunk.Tests;

public sealed class GitHubCopilotClientTests
{
    [Fact]
    public async Task SendMessageAsync_成功時回傳執行器內容()
    {
        var executor = new StubExecutor
        {
            Response = "public class Generated {}"
        };
        await using var client = new GitHubCopilotClient(executor);

        var result = await client.SendMessageAsync("gpt-5", "system", "user");

        Assert.Equal("public class Generated {}", result);
        Assert.Equal("gpt-5", executor.Model);
        Assert.Equal("system", executor.SystemPrompt);
        Assert.Equal("user", executor.UserPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_模型名稱為空時拋出例外()
    {
        await using var client = new GitHubCopilotClient(new StubExecutor());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendMessageAsync("", "system", "user"));
    }

    [Fact]
    public async Task DisposeAsync_會轉呼叫執行器()
    {
        var executor = new StubExecutor();
        var client = new GitHubCopilotClient(executor);

        await client.DisposeAsync();

        Assert.True(executor.DisposeCalled);
    }

    [Fact]
    public void CreateOptions_會保留基本設定()
    {
        var options = GitHubCopilotClient.CreateOptions("token", "/usr/local/bin/copilot", "/tmp/work");

        Assert.Equal("/usr/local/bin/copilot", options.CliPath);
        Assert.Equal("/tmp/work", options.Cwd);
        Assert.Equal("token", options.GitHubToken);
    }

    [Fact]
    public void CreateCliEnvironment_LinuxBundledCli時會確保快取目錄可用()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var uniqueRoot = Path.Combine(Path.GetTempPath(), "skt-vegapunk-tests", Guid.NewGuid().ToString("N"));
        var originalXdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");

        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", uniqueRoot);

            var environment = GitHubCopilotClient.CreateCliEnvironment(null);
            var expectedDirectory = Path.Combine(
                uniqueRoot,
                "copilot",
                "pkg",
                RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64");

            Assert.Null(environment);
            Assert.True(Directory.Exists(expectedDirectory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", originalXdgCacheHome);
            if (Directory.Exists(uniqueRoot))
            {
                Directory.Delete(uniqueRoot, recursive: true);
            }
        }
    }

    private sealed class StubExecutor : IGitHubCopilotExecutor
    {
        public string? Model { get; private set; }

        public string? SystemPrompt { get; private set; }

        public string? UserPrompt { get; private set; }

        public TimeSpan? Timeout { get; private set; }

        public string? Response { get; init; }

        public bool DisposeCalled { get; private set; }

        public Task<string?> ExecuteAsync(
            string model,
            string systemPrompt,
            string userPrompt,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            Model = model;
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            Timeout = timeout;
            return Task.FromResult(Response);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
