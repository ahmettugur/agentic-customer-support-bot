// Models/ChatRequest.cs
// Chat endpoint'lerinin kabul ettiği istek modeli.

using System.Text.Json.Serialization;

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Kullanıcının gönderdiği chat isteği.
/// </summary>
public record ChatRequest(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("sessionId")] string? SessionId = null
);
