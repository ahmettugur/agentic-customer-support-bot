// Ports/Outbound/Persistence/IConversationDispositionStore.cs
// Konuşma kapanış kayıtları. Uygulamalar ISessionDataEraser'ı da uygular (KVKK).

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface IConversationDispositionStore
{
    Task AddAsync(ConversationDisposition disposition, CancellationToken ct = default);

    /// <summary>Bir oturumun kapanış kayıtları, eskiden yeniye (sohbet birden çok kez devralınabilir).</summary>
    Task<IReadOnlyList<ConversationDisposition>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> CountByReasonAsync(CancellationToken ct = default);

    /// <summary>En sık etiketler: çoktan aza, eşitlikte ada göre.</summary>
    Task<IReadOnlyList<TagUsage>> TopTagsAsync(int limit, CancellationToken ct = default);
}
