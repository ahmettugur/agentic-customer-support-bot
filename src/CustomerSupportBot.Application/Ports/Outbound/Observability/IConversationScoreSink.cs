// Ports/Outbound/Observability/IConversationScoreSink.cs
// Görüşme kalitesi sinyallerinin (müşteri puanı) LLM gözlem aracına skor olarak gönderilmesi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// Müşteri puanını gözlem aracına (Langfuse) görüşme bazlı skor olarak yazar; böylece düşük puanlı bir
/// görüşmenin trace'leri tek tıkla bulunur. En iyi gayret: çağıran hataları yutar, puan kaydı buna bağlı değildir.
/// </summary>
public interface IConversationScoreSink
{
    Task PublishRatingAsync(ConversationRating rating, CancellationToken ct = default);

    /// <summary>Mesaj başına 👍/👎'yi oturum skoru olarak yazar (kimlik: oturum + mesaj özeti; yeniden verince güncellenir).</summary>
    Task PublishMessageFeedbackAsync(MessageFeedback feedback, CancellationToken ct = default);

    /// <summary>Daha önce yazılmış mesaj skorunu kaldırır (oy geri çekildi / KVKK silme). Olmayan skor hata değildir.</summary>
    Task RemoveMessageFeedbackAsync(string sessionId, string messageHash, CancellationToken ct = default);
}
