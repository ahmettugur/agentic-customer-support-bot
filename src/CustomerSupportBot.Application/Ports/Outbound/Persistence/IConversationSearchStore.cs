// Ports/Outbound/Persistence/IConversationSearchStore.cs
// Konuşma araması — tek sorgu (oturum başına sorgu yok).

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <param name="FoldedText">Türkçe katlanmış arama metni (bkz. <c>TurkishText.Fold</c>); depo aynı katlamayı uygular.</param>
/// <param name="FromUtc">Dahil.</param>
/// <param name="ToUtc">Hariç.</param>
public sealed record ConversationSearchCriteria(
    string? FoldedText, string? CustomerId, DateTime? FromUtc, DateTime? ToUtc, string? ReasonCode, string? Tag, int Offset, int Take);

/// <param name="MatchedText">Metni içeren ilk mesaj; metin aranmadıysa ilk müşteri mesajı.</param>
public sealed record ConversationSearchRow(
    string SessionId,
    string? CustomerId,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    string? MatchedText,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Tags);

public interface IConversationSearchStore
{
    /// <summary>Son etkinliğe göre yeniden eskiye.</summary>
    Task<IReadOnlyList<ConversationSearchRow>> SearchAsync(ConversationSearchCriteria criteria, CancellationToken ct = default);
}
