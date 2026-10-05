// Application/Services/Conversations/ConversationSearchService.cs
// Konuşma araması: doğrulama, normalleştirme, sayfalama, vurgulu alıntı.

using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Application.Services.Conversations;

public sealed partial class ConversationSearchService(IConversationSearchStore store) : IConversationSearchPort
{
    public const int PageSize = 25;
    public const int SnippetLength = 160;
    public const int MinTextLength = 2;
    public const int MaxTextLength = 200;
    private const int MaxPage = 10_000;
    /// <summary>Alıntıda eşleşmeden önce bırakılan bağlam.</summary>
    private const int Lead = 60;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<ConversationSearchResult> SearchAsync(ConversationSearchQuery query, CancellationToken ct = default)
    {
        var text = Blank(query.Text);
        if (text is { Length: < MinTextLength })
            return new ConversationSearchResult(null, $"Arama metni en az {MinTextLength} karakter olmalı.");
        if (text is { Length: > MaxTextLength })
            return new ConversationSearchResult(null, $"Arama metni en fazla {MaxTextLength} karakter olabilir.");
        if (query.From is { } from && query.To is { } to && from > to)
            return new ConversationSearchResult(null, "Başlangıç tarihi bitişten sonra olamaz.");

        var page = Math.Clamp(query.Page, 1, MaxPage);
        var folded = text is null ? null : TurkishText.Fold(text);
        var tag = Blank(query.Tag) is { } rawTag ? Blank(TurkishText.NormalizeTag(rawTag)) : null;
        var rows = await store.SearchAsync(new ConversationSearchCriteria(
            folded, Blank(query.CustomerId), query.From, query.To, Blank(query.Reason), tag,
            (page - 1) * PageSize, PageSize + 1), ct);

        var items = rows.Take(PageSize).Select(r =>
        {
            var (snippet, start, length) = Excerpt(r.MatchedText, folded);
            return new ConversationSearchHit(r.SessionId, r.CustomerId, r.CreatedAt, r.LastActivity, r.MessageCount,
                snippet, start, length, r.Reasons, r.Tags);
        }).ToList();
        return new ConversationSearchResult(new ConversationSearchPage(items, page, PageSize, rows.Count > PageSize), null);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Eşleşmenin çevresinden en fazla <see cref="SnippetLength"/> karakter; kesilen uçlara "…". Eşleşme
    /// katlanmış metinde aranır (Türkçe harf farkları), katlama uzunluğu koruduğu için konum özgün metinde
    /// de geçerlidir.
    /// </summary>
    private static (string? Snippet, int? Start, int? Length) Excerpt(string? raw, string? foldedTerm)
    {
        if (raw is null) return (null, null, null);
        var text = Whitespace().Replace(raw.Trim(), " ");
        var index = foldedTerm is null ? -1 : TurkishText.Fold(text).IndexOf(foldedTerm, StringComparison.Ordinal);
        if (index < 0)
            return (text.Length <= SnippetLength ? text : text[..SnippetLength] + "…", null, null);
        if (text.Length <= SnippetLength)
            return (text, index, foldedTerm!.Length);

        var start = Math.Max(0, index - Lead);
        if (start + SnippetLength > text.Length) start = Math.Max(0, text.Length - SnippetLength);
        var end = Math.Min(text.Length, start + SnippetLength);
        var prefix = start > 0 ? "…" : "";
        var suffix = end < text.Length ? "…" : "";
        var length = Math.Min(foldedTerm!.Length, end - index);
        return (prefix + text[start..end] + suffix, index - start + prefix.Length, length);
    }
}
