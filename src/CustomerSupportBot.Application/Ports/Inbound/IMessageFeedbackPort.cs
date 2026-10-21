// Ports/Inbound/IMessageFeedbackPort.cs
// Bot yanıtlarına 👍/👎 geri bildirimi use case'i.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <param name="Text">Geri bildirim verilen bot mesajının metni (sunucu, oturumun geçmişinde olduğunu doğrular).</param>
/// <param name="Helpful"><c>true</c> 👍, <c>false</c> 👎, <c>null</c> oyu geri çek.</param>
public sealed record MessageFeedbackRequest(string Text, bool? Helpful, string? Reason, string? Comment);

public enum MessageFeedbackOutcome { Saved, Cleared, MessageNotFound, Invalid }

public sealed record MessageFeedbackResult(MessageFeedbackOutcome Outcome, string? Error = null);

public interface IMessageFeedbackPort
{
    Task<MessageFeedbackResult> SubmitAsync(string sessionId, string? customerId, MessageFeedbackRequest request, CancellationToken ct = default);

    /// <summary>Oturumdaki oylar, mesaj özetine göre.</summary>
    Task<IReadOnlyDictionary<string, MessageFeedback>> GetForSessionAsync(string sessionId, CancellationToken ct = default);
}
