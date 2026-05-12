// Models/ChatResponse.cs
// /chat/ endpoint'inin döndürdüğü yanıt modeli (non-streaming).

using System.Text.Json.Serialization;

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Non-streaming chat yanıtı. Reasoning alanı opsiyoneldir.
/// </summary>
public record ChatResponse(
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("reasoning")] ReasoningResult? Reasoning = null
);
