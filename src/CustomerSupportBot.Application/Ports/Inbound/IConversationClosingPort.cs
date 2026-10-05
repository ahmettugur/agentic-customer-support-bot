// Ports/Inbound/IConversationClosingPort.cs
// Temsilcinin canlı sohbeti kapanış nedeni, etiketler ve notla kapatması.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public sealed record ConversationClosingInput(string? Reason, IReadOnlyList<string>? Tags, string? Note);

public sealed record ClosingReasonOption(string Code, string Label);

public sealed record ConversationClosingOptionsView(
    bool RequireReason, IReadOnlyList<ClosingReasonOption> Reasons, IReadOnlyList<string> SuggestedTags);

public enum ConversationClosingStatus { Ok, Invalid, NotLive }

public sealed record ConversationClosingResult(
    ConversationClosingStatus Status,
    ConversationDisposition? Disposition = null,
    int EscalationsResolved = 0,
    string? Error = null);

public interface IConversationClosingPort
{
    /// <summary>Kapanış penceresi için: zorunluluk, nedenler, önerilen (en sık) etiketler.</summary>
    Task<ConversationClosingOptionsView> GetOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Doğrular, sohbeti bırakır (Bot moduna dönüş, eskalasyonların çözülmesi, temsilci yükü) ve kaydı
    /// yazar. Geçersiz istek sohbeti kapatmaz. <paramref name="agentId"/> temsilcinin yükünü düşürmek için.
    /// </summary>
    Task<ConversationClosingResult> CloseAsync(
        string sessionId, ConversationClosingInput input, string? closedBy, string? agentId, CancellationToken ct = default);
}
