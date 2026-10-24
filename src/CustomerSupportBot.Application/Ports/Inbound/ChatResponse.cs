// Ports/Driving/ChatResponse.cs
// IChatPort driving port'unun use case output DTO'su.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Non-streaming chat yanıtı — use case boundary output.
/// </summary>
public record ChatResponse(string Response, string SessionId, CustomerReasoningView? Reasoning = null);
