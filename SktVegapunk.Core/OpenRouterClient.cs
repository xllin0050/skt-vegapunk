using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SktVegapunk.Core;

public class OpenRouterClient
{
    private readonly HttpClient _httpClient;
    private const string _apiUrl = "https://openrouter.ai/api/v1/chat/completions";

    public OpenRouterClient(
        HttpClient httpClient,
        string apiKey,
        string referer = "http://localhost",
        string title = "SktVegapunk Agent")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(referer);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _httpClient = httpClient;

        // 1. 設定 Authorization Header
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 2. OpenRouter 官方建議加入這兩個 Header，用於在其儀表板上識別你的 App
        _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        _httpClient.DefaultRequestHeaders.Remove("X-Title");
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", referer);
        _httpClient.DefaultRequestHeaders.Add("X-Title", title);
    }

    /// <summary>
    /// 發送訊息給 OpenRouter 並取得回覆
    /// </summary>
    public async Task<string?> SendMessageAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        // 組合請求資料
        var requestBody = new ChatRequest(
            Model: model,
            Messages: new List<ChatMessage>
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
            }
        );

        // 發送 POST 請求並自動將物件序列化為 JSON
        var response = await _httpClient.PostAsJsonAsync(_apiUrl, requestBody, cancellationToken);

        // 如果 API 回傳 4xx 或 5xx 錯誤，這裡會拋出 Exception，方便我們除錯
        response.EnsureSuccessStatusCode();

        // 讀取回應並自動反序列化為 C# 物件
        var responseBody = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);

        // 提取 AI 生成的文字
        return responseBody?.Choices.FirstOrDefault()?.Message.Content;
    }
}
