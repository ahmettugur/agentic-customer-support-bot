// Models/ChatResponse.cs
// /chat/ endpoint'inin döndürdüğü yanıt modeli (non-streaming).

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Non-streaming chat yanıtı. Reasoning alanı opsiyoneldir.
/// </summary>
public record ChatResponse(string Response, string SessionId, ReasoningResult? Reasoning = null);

