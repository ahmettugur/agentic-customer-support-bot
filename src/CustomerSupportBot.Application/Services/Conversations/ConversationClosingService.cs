// Application/Services/Conversations/ConversationClosingService.cs
// Canlı sohbetin kapanış nedeni, etiketler ve notla kapatılması.

using System.Globalization;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Conversations;

/// <summary>
/// <see cref="IConversationClosingPort"/> uygulaması. Sıra önemlidir: önce doğrulama (geçersiz istek
/// sohbeti kapatmaz), sonra mevcut bırakma akışı (<see cref="IChatSessionPort.ReleaseAsync"/>), en son
/// kayıt. Kayıt yazılamazsa sohbet yine kapanmış olur; sonuç bunu <c>Error</c> ile bildirir.
/// </summary>
public sealed partial class ConversationClosingService(
    IChatSessionPort chatSessions,
    IConversationDispositionStore store,
    IOptions<ConversationClosingOptions> options,
    TimeProvider? clock = null,
    ILogger<ConversationClosingService>? logger = null) : IConversationClosingPort
{
    private const int SuggestedTagCount = 10;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly ConversationClosingOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<ConversationClosingService>.Instance;

    [GeneratedRegex(@"^[\p{L}\p{Nd}_-]+$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<ConversationClosingOptionsView> GetOptionsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> suggested;
        try
        {
            suggested = (await store.TopTagsAsync(SuggestedTagCount, ct)).Select(t => t.Tag).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Öneriler olmadan da kapatılabilir — pencere açılmamazlık etmesin.
            _logger.LogWarning(ex, "[Closing] Önerilen etiketler okunamadı");
            suggested = [];
        }
        return new ConversationClosingOptionsView(_options.RequireReason, _options.EffectiveReasons, suggested);
    }

    public async Task<ConversationClosingResult> CloseAsync(
        string sessionId, ConversationClosingInput input, string? closedBy, string? agentId, CancellationToken ct = default)
    {
        var (error, reasonCode, tags, note) = Validate(input);
        if (error is not null) return new ConversationClosingResult(ConversationClosingStatus.Invalid, Error: error);

        var release = await chatSessions.ReleaseAsync(sessionId, agentId);
        if (!release.Success)
            return new ConversationClosingResult(ConversationClosingStatus.NotLive, Error: release.ErrorMessage ?? "Sohbet canlı değil.");

        if (reasonCode.Length == 0 && tags.Count == 0 && note is null)
            return new ConversationClosingResult(ConversationClosingStatus.Ok, EscalationsResolved: release.EscalationsResolved);

        var disposition = new ConversationDisposition
        {
            SessionId = sessionId, ReasonCode = reasonCode, Tags = tags, Note = note,
            ClosedBy = closedBy, ClosedAt = _clock.GetUtcNow().UtcDateTime
        };
        try
        {
            await store.AddAsync(disposition, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Closing] Kapanış kaydı yazılamadı | session={SessionId}", sessionId);
            return new ConversationClosingResult(ConversationClosingStatus.Ok, EscalationsResolved: release.EscalationsResolved,
                Error: "Sohbet kapatıldı ancak kapanış kaydı yazılamadı.");
        }
        return new ConversationClosingResult(ConversationClosingStatus.Ok, disposition, release.EscalationsResolved);
    }

    private (string? Error, string ReasonCode, List<string> Tags, string? Note) Validate(ConversationClosingInput input)
    {
        var reasonCode = "";
        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            if (_options.RequireReason) return ("Kapanış nedeni seçin.", "", [], null);
        }
        else
        {
            var match = _options.EffectiveReasons.FirstOrDefault(r => string.Equals(r.Code, reason, StringComparison.OrdinalIgnoreCase));
            if (match is null) return ($"Bilinmeyen kapanış nedeni: {reason}", "", [], null);
            reasonCode = match.Code;
        }

        var tags = new List<string>();
        foreach (var raw in input.Tags ?? [])
        {
            var tag = NormalizeTag(raw);
            if (tag.Length == 0 || tags.Contains(tag)) continue;
            if (tag.Length > ConversationDisposition.MaxTagLength)
                return ($"Etiket en fazla {ConversationDisposition.MaxTagLength} karakter olabilir: {tag}", "", [], null);
            if (!TagPattern().IsMatch(tag))
                return ($"Etiket yalnızca harf, rakam, '-' ve '_' içerebilir: {tag}", "", [], null);
            tags.Add(tag);
        }
        if (tags.Count > ConversationDisposition.MaxTags)
            return ($"En fazla {ConversationDisposition.MaxTags} etiket eklenebilir.", "", [], null);

        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (note is { Length: > ConversationDisposition.MaxNoteLength })
            return ($"Not en fazla {ConversationDisposition.MaxNoteLength} karakter olabilir.", "", [], null);

        return (null, reasonCode, tags, note);
    }

    /// <summary>
    /// Küçük harf (Türkçe), boşluklar <c>-</c>. <c>I</c> ve <c>İ</c> önce <c>i</c>'ye çevrilir: yalnızca Türkçe
    /// küçük harf "IADE"yi "ıade" yapar ve "iade" ile ayrı etiket sayılırdı.
    /// </summary>
    internal static string NormalizeTag(string? raw) =>
        Whitespace().Replace((raw ?? "").Trim().Replace('I', 'i').Replace('İ', 'i').ToLower(Turkish), "-");
}
