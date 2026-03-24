using SktVegapunk.Core;

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

    private sealed class StubExecutor : IGitHubCopilotExecutor
    {
        public string? Model { get; private set; }

        public string? SystemPrompt { get; private set; }

        public string? UserPrompt { get; private set; }

        public string? Response { get; init; }

        public bool DisposeCalled { get; private set; }

        public Task<string?> ExecuteAsync(
            string model,
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken)
        {
            Model = model;
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            return Task.FromResult(Response);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
