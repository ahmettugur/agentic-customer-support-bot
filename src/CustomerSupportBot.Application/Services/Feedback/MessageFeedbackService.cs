// Application/Services/Feedback/MessageFeedbackService.cs
// Bot yanıtına 👍/👎 — doğrulama, trace bağlama, kalıcılık ve gözlem aracına (en iyi gayret) yazım.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Feedback;

public sealed class MessageFeedbackService : IMessageFeedbackPort
{
    /// <summary>Bir bot yanıtı için kabul edilen azami metin uzunluğu (istek şişirmeyi önler).</summary>
    public const int MaxTextLength = 8000;

    // Uygulamanın ortak Meter adı (TelemetryConstants.MeterName): OpenTelemetry bunu zaten dinler → Prometheus/Grafana.
    private static readonly Meter Meter = new(TelemetryConstants.MeterName, "1.0.0");
    private static readonly Counter<long> VotesCounter = Meter.CreateCounter<long>(
        "chat.message.feedback", unit: "{vote}", description: "Bot yanıtlarına verilen 👍/👎 (helpful, reason).");

    private static readonly TimeSpan SinkTimeout = TimeSpan.FromSeconds(3);

    private readonly IMessageFeedbackStore _store;
    private readonly ISessionManager _sessions;
    private readonly IReasoningTraceStore _traces;
    private readonly ILogger<MessageFeedbackService> _logger;
    private readonly IConversationScoreSink? _sink;

    public MessageFeedbackService(
        IMessageFeedbackStore store,
        ISessionManager sessions,
        IReasoningTraceStore traces,
        ILogger<MessageFeedbackService> logger,
        IConversationScoreSink? sink = null)
    {
        _store = store;
        _sessions = sessions;
        _traces = traces;
        _logger = logger;
        _sink = sink;
    }

    public async Task<MessageFeedbackResult> SubmitAsync(
        string sessionId, string? customerId, MessageFeedbackRequest request, CancellationToken ct = default)
    {
        // Ucuz doğrulamalar oturum aramasından ÖNCE.
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxTextLength)
            return Invalid("Mesaj metni boş ya da çok uzun.");
        if (request.Helpful == false && request.Reason is not null && !MessageFeedbackReasons.IsValid(request.Reason))
            return Invalid("Geçersiz neden.");
        if (request.Comment is { Length: > MessageFeedback.MaxCommentLength })
            return Invalid($"Yorum en fazla {MessageFeedback.MaxCommentLength} karakter olabilir.");

        var hash = MessageFeedback.HashOf(request.Text);

        // Yalnızca bu oturumun GERÇEK bir bot mesajına geri bildirim verilebilir: kullanıcının kendi mesajına ya da
        // uydurma bir metne verilemez (keyfi satır üretimini ve başka oturum metni yoklamayı engeller).
        var history = await _sessions.GetHistoryAsync(sessionId, ct);
        var isBotMessage = history.Any(m => m.Role == ConversationRoles.Assistant && MessageFeedback.HashOf(m.Text ?? "") == hash);
        if (!isBotMessage) return new(MessageFeedbackOutcome.MessageNotFound);

        if (request.Helpful is null)
        {
            await _store.RemoveAsync(sessionId, hash, ct);
            await BestEffortAsync((sink, token) => sink.RemoveMessageFeedbackAsync(sessionId, hash, token), sessionId);
            return new(MessageFeedbackOutcome.Cleared);
        }

        var feedback = new MessageFeedback
        {
            SessionId = sessionId,
            MessageHash = hash,
            Helpful = request.Helpful.Value,
            // Neden ve yorum yalnızca 👎'de anlamlıdır.
            Reason = request.Helpful == false ? request.Reason : null,
            Comment = request.Helpful == false && !string.IsNullOrWhiteSpace(request.Comment) ? request.Comment.Trim() : null,
            TraceId = FindTrace(sessionId, hash),
            CustomerId = customerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _store.UpsertAsync(feedback, ct);
        VotesCounter.Add(1,
            new KeyValuePair<string, object?>("helpful", feedback.Helpful ? "true" : "false"),
            new KeyValuePair<string, object?>("reason", feedback.Reason ?? "none"));
        await BestEffortAsync((sink, token) => sink.PublishMessageFeedbackAsync(feedback, token), sessionId);
        return new(MessageFeedbackOutcome.Saved);
    }

    public async Task<IReadOnlyDictionary<string, MessageFeedback>> GetForSessionAsync(string sessionId, CancellationToken ct = default) =>
        (await _store.GetForSessionAsync(sessionId, ct)).ToDictionary(f => f.MessageHash, StringComparer.Ordinal);

    /// <summary>Yanıtı üreten tur: oturumun trace'leri arasında <c>FinalResponse</c>'u bu metinle aynı olan en yeni.</summary>
    private string? FindTrace(string sessionId, string hash) =>
        _traces.GetBySession(sessionId)
            .Where(t => !string.IsNullOrEmpty(t.FinalResponse) && MessageFeedback.HashOf(t.FinalResponse) == hash)
            .OrderByDescending(t => t.StartedAt)
            .FirstOrDefault()?.TraceId;

    /// <summary>Gözlem aracı geri bildirimin kaydı için şart değildir: kısa süreyle sınırlı, hata yutulur.</summary>
    private async Task BestEffortAsync(Func<IConversationScoreSink, CancellationToken, Task> action, string sessionId)
    {
        if (_sink is null) return;
        using var timeout = new CancellationTokenSource(SinkTimeout);
        try { await action(_sink, timeout.Token); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesaj geri bildirimi gözlem aracına yazılamadı (session={SessionId})", sessionId);
        }
    }

    private static MessageFeedbackResult Invalid(string error) => new(MessageFeedbackOutcome.Invalid, error);
}
