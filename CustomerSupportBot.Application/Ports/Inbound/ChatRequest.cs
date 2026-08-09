// Ports/Driving/ChatRequest.cs
// IChatPort driving port'unun use case input DTO'su.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Kullanıcının gönderdiği chat isteği — use case boundary input.
/// </summary>
/// <param name="CustomerId">
/// Login'li müşterinin doğrulanmış kimliği — API katmanı bunu JWT claim'inden doldurur,
/// asla client body'sinden GÜVENİLİR olarak alınmaz (endpoint bu alanı isteğin geldiği
/// body'den değil, kimlik doğrulanmış HttpContext.User'dan set eder).
/// </param>
public record ChatRequest(string Query, string? SessionId = null, string? CustomerId = null);
