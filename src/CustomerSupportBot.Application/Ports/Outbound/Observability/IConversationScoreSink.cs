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
}
