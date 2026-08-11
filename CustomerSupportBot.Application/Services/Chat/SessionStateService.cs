// Application/Services/SessionStateService.cs
// Session state yönetimi — sentiment güncellemesi, intent güncellemesi, sentiment alert kontrolü.
// ChatStreamOrchestrator (Api) bu servisi kullanır; iş mantığı Application katmanında kalır.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Oturum durumu iş mantığı — sentiment güncelleme, intent yönetimi, alert kontrolü.
/// Transport katmanından (SSE, WebSocket) bağımsızdır.
/// </summary>
public sealed class SessionStateService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionStateService> _logger;

    public SessionStateService(
        ISessionManager sessionManager,
        ILogger<SessionStateService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Konuşmayı (exchange) session history'ye kaydeder, turun state çıkarımını tetikler
    /// ve ChatBridge'e bildirir.
    ///
    /// <para>
    /// <b>Intent ve sentiment burada, turun kapanışında tek seferde işlenir.</b> Eskiden
    /// bunlar için ayrı <c>UpdateSessionIntentAsync</c> / <c>UpdateSessionSentiment</c>
    /// metotları vardı ve tur ortasında state'e yazıyorlardı; hemen ardından bu çağrı
    /// (<c>AddExchangeAsync</c> → <c>SessionStateExtractor</c>) aynı alanları kural tabanlı
    /// değerlerle bir kez daha yazıyordu. Sonuç: LLM'in kararı her turda eziliyor,
    /// <c>ConsecutiveNegativeTurns</c> ise tur başına iki kez artıyordu. Artık LLM'in
    /// ürettikleri <see cref="TurnSignals"/> olarak GİRDİ biçiminde taşınır; türetilmiş
    /// alanların tek yazarı <c>SessionStateExtractor.ExtractAndApply</c>'dır.
    /// </para>
    /// </summary>
    /// <param name="signals">
    /// Bu tur için LLM sinyalleri; <c>null</c> ise kural tabanlı çıkarım kullanılır.
    /// </param>
    public async Task PersistExchangeAsync(
        string sessionId,
        string query,
        string response,
        IChatBridge chatBridge,
        TurnSignals? signals = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(response))
        {
            await _sessionManager.AddExchangeAsync(sessionId, query, response, signals, ct);
            chatBridge.RecordBotExchange(sessionId, query, response);
        }
    }

    /// <summary>
    /// Sentiment alert gerekip gerekmediğini kontrol eder.
    /// Ardışık negatif tur sayısı eşiği aşarsa true döner.
    /// </summary>
    public SentimentAlertResult CheckSentimentAlert(AgentSession session)
    {
        SentimentAlertResult result;
        lock (session)
        {
            var state = session.State;
            result = new SentimentAlertResult
            {
                Sentiment = state.Sentiment,
                Score = state.SentimentScore,
                ConsecutiveNegativeTurns = state.ConsecutiveNegativeTurns,
                SessionId = session.SessionId,
                ShouldAlert = state.ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative
            };
        }

        if (result.ShouldAlert)
        {
            _logger.LogWarning(
                "[Sentiment Alert] Session {SessionId}: {Consecutive} ardışık negatif tur (skor: {Score})",
                session.SessionId, result.ConsecutiveNegativeTurns, result.Score);
        }

        return result;
    }
}

/// <summary>Sentiment alert sonucu — transport-bağımsız DTO.</summary>
public sealed class SentimentAlertResult
{
    public string? Sentiment { get; init; }
    public double Score { get; init; }
    public int ConsecutiveNegativeTurns { get; init; }
    public string SessionId { get; init; } = "";
    public bool ShouldAlert { get; init; }
    public string AlertMessage =>
        $"Müşteri {ConsecutiveNegativeTurns} tur boyunca olumsuz. Bir temsilci bağlanmalı.";
}
