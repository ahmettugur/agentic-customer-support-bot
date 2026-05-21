// Ports/Driving/ChatResponse.cs
// IChatPort driving port'unun use case output DTO'su.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Non-streaming chat yanıtı — use case boundary output.
/// </summary>
public record ChatResponse(string Response, string SessionId, ReasoningResult? Reasoning = null);
