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
    /// Reasoning sonucundaki intent'i session state'e yazar.
    /// </summary>
    public void UpdateSessionIntent(AgentSession session, string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent) || intent == WellKnown.Intents.Unknown) return;

        // Aynı session'a çakışan (çift-submit, çoklu sekme) eşzamanlı isteklerde
        // state mutasyonu — bkz. UpdateSessionSentiment'teki ConsecutiveNegativeTurns
        // yorumu için aynı gerekçe. ISessionManager.GetOrCreate/Get aynı sessionId için
        // hep AYNI AgentSession referansını döndürür, bu yüzden session nesnesinin
        // kendisi kilit anahtarı olarak güvenle kullanılabilir (bkz.
        // WorkflowRunner.ConsumeForceReplanHint'teki aynı kalıp).
        lock (session)
        {
            session.State.CurrentIntent = intent;
        }
        _sessionManager.Update(session);
    }

    /// <summary>
    /// LLM reasoning sonucundaki sentiment'i session state'e yazar.
    /// Kural tabanlı sonucu override eder (LLM daha doğru).
    /// </summary>
    public void UpdateSessionSentiment(AgentSession session, ReasoningResult reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning.Sentiment) ||
            reasoning.Sentiment == WellKnown.Sentiments.Neutral && reasoning.SentimentScore == 0.5)
        {
            return; // LLM sentiment döndürmemiş, kural tabanlı sonucu koru
        }

        // ConsecutiveNegativeTurns++ oku-değiştir-yaz — kilitsiz olursa aynı session'a
        // çakışan iki eşzamanlı istek (çift-submit, çoklu sekme) birbirinin artışını
        // ezebilir ve otomatik eskalasyon eşiği bir tur geç tetiklenir/hiç tetiklenmez.
        lock (session)
        {
            var state = session.State;
            state.Sentiment = reasoning.Sentiment;
            state.SentimentScore = reasoning.SentimentScore;

            if (reasoning.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
                state.ConsecutiveNegativeTurns++;
            else
                state.ConsecutiveNegativeTurns = 0;
        }
    }

    /// <summary>
    /// Konuşmayı (exchange) session history'ye kaydeder ve ChatBridge'e bildirir.
    /// </summary>
    public void PersistExchange(
        string sessionId,
        string query,
        string response,
        IChatBridge chatBridge)
    {
        if (!string.IsNullOrWhiteSpace(response))
        {
            _sessionManager.AddExchange(sessionId, query, response);
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
