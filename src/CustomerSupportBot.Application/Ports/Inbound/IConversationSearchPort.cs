// Ports/Inbound/IConversationSearchPort.cs
// Yönetici konuşma araması: metin, müşteri, tarih aralığı, kapanış nedeni, etiket.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <param name="From">Dahil, UTC.</param>
/// <param name="To">Hariç, UTC.</param>
/// <param name="Page">1'den başlar.</param>
public sealed record ConversationSearchQuery(
    string? Text, string? CustomerId, DateTime? From, DateTime? To, string? Reason, string? Tag, int Page = 1);

/// <param name="Snippet">Eşleşen mesajdan alıntı; metin aranmadıysa ilk müşteri mesajı.</param>
/// <param name="HighlightStart">Alıntıda vurgulanacak eşleşmenin konumu (metin aranmadıysa null).</param>
public sealed record ConversationSearchHit(
    string SessionId,
    string? CustomerId,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    string? Snippet,
    int? HighlightStart,
    int? HighlightLength,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Tags);

public sealed record ConversationSearchPage(IReadOnlyList<ConversationSearchHit> Items, int Page, int PageSize, bool HasMore);

/// <summary><c>Error</c> doluysa sorgu geçersizdir (uçta 400), <c>Page</c> boştur.</summary>
public sealed record ConversationSearchResult(ConversationSearchPage? Page, string? Error);

public interface IConversationSearchPort
{
    Task<ConversationSearchResult> SearchAsync(ConversationSearchQuery query, CancellationToken ct = default);
}
