// Ports/Driving/ChatRequest.cs
// IChatPort driving port'unun use case input DTO'su.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Kullanıcının gönderdiği chat isteği — use case boundary input.
/// </summary>
public record ChatRequest(string Query, string? SessionId = null);
