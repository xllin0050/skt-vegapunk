using System.Text.Json.Serialization;

namespace SktVegapunk.Core;

// 請求的 Payload
public record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<ChatMessage> Messages
);

public record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

// 回應的 Payload (擷取我們需要的欄位即可)
public record ChatResponse(
    [property: JsonPropertyName("choices")] List<ChatChoice> Choices
);

public record ChatChoice(
    [property: JsonPropertyName("message")] ChatMessage Message
);
