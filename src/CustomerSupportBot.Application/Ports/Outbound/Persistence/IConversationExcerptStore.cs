using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Bir konuşmanın belirli bir andan önceki son mesajları — geçmiş sırasıyla (eskiden yeniye).</summary>
public interface IConversationExcerptStore
{
    Task<IReadOnlyList<ConversationMessage>> GetBeforeAsync(
        string sessionId, DateTime beforeUtc, int count, CancellationToken ct = default);
}
