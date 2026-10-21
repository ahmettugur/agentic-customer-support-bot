// Ports/Outbound/Persistence/IMessageFeedbackStore.cs
// Mesaj başına 👍/👎 geri bildirimlerinin deposu.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface IMessageFeedbackStore
{
    /// <summary>(oturum, mesaj özeti) için ekler ya da günceller; ilk <c>CreatedAt</c> korunur.</summary>
    Task UpsertAsync(MessageFeedback feedback, CancellationToken ct = default);

    /// <summary>Geri bildirimi kaldırır (müşteri oyunu geri çekti). Olmayan kayıt hata değildir.</summary>
    Task RemoveAsync(string sessionId, string messageHash, CancellationToken ct = default);

    Task<IReadOnlyList<MessageFeedback>> GetForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>En yeniden eskiye; <paramref name="helpful"/> verilirse yalnızca o yönde olanlar.</summary>
    Task<IReadOnlyList<MessageFeedback>> GetRecentAsync(int count, bool? helpful = null, CancellationToken ct = default);

    /// <summary>Verilen tarihten sonraki 👎'ler (öğrenme sinyali).</summary>
    Task<IReadOnlyList<MessageFeedback>> GetUnhelpfulSinceAsync(DateTime sinceUtc, int max, CancellationToken ct = default);
}
