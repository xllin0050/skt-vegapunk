using System.Net;
using System.Text;
using SktVegapunk.Core;

namespace SktVegapunk.Tests;

public sealed class OpenRouterClientTests
{
    [Fact]
    public async Task SendMessageAsync_成功時回傳第一個Choice內容()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var responseBody = """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "public class Generated {}"
                      }
                    }
                  ]
                }
                """;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var client = new OpenRouterClient(httpClient, "test-key", "http://localhost/test", "SktVegapunk Test");

        var result = await client.SendMessageAsync("qwen/qwen3-coder", "system", "user");

        Assert.Equal("public class Generated {}", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Equal("http://localhost/test", handler.LastRequest.Headers.GetValues("HTTP-Referer").Single());
        Assert.Equal("SktVegapunk Test", handler.LastRequest.Headers.GetValues("X-Title").Single());
    }

    [Fact]
    public async Task SendMessageAsync_非成功狀態時拋出例外()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server error", Encoding.UTF8, "text/plain")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var client = new OpenRouterClient(httpClient, "test-key");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendMessageAsync("qwen/qwen3-coder", "system", "user"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _responder(request, cancellationToken);
        }
    }
}
