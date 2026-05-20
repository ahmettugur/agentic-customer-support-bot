// Models/ChatRequest.cs
// Chat endpoint'lerinin kabul ettiği istek modeli.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Kullanıcının gönderdiği chat isteği.
/// </summary>
public record ChatRequest(string Query, string? SessionId = null);

