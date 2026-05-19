// Application/Services/SessionStateService.cs
// Session state yönetimi — sentiment güncellemesi, intent güncellemesi, sentiment alert kontrolü.
// ChatStreamOrchestrator (Api) bu servisi kullanır; iş mantığı Application katmanında kalır.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Oturum durumu iş mantığı — sentiment güncelleme, intent yönetimi, alert kontrolü.
/// Transport katmanından (SSE, WebSocket) bağımsızdır.
/// </summary>
public sealed class SessionStateService
{
    private readonly ISessionRepository _sessionManager;
    private readonly ILogger<SessionStateService> _logger;

    public SessionStateService(
        ISessionRepository sessionManager,
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
        if (!string.IsNullOrWhiteSpace(intent) && intent != WellKnown.Intents.Unknown)
        {
            session.State.CurrentIntent = intent;
            _sessionManager.Update(session);
        }
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

        var state = session.State;
        state.Sentiment = reasoning.Sentiment;
        state.SentimentScore = reasoning.SentimentScore;

        // Ardışık negatif sayacını güncelle
        if (reasoning.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
            state.ConsecutiveNegativeTurns++;
        else
            state.ConsecutiveNegativeTurns = 0;
    }

    /// <summary>
    /// Konuşmayı (exchange) session history'ye kaydeder ve ChatBridge'e bildirir.
    /// </summary>
    public void PersistExchange(
        string sessionId,
        string query,
        string response,
        IChatBridgeRepository chatBridge)
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
        var state = session.State;
        var result = new SentimentAlertResult
        {
            Sentiment = state.Sentiment,
            Score = state.SentimentScore,
            ConsecutiveNegativeTurns = state.ConsecutiveNegativeTurns,
            SessionId = session.SessionId,
            ShouldAlert = state.ConsecutiveNegativeTurns >= WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative
        };

        if (result.ShouldAlert)
        {
            _logger.LogWarning(
                "[Sentiment Alert] Session {SessionId}: {Consecutive} ardışık negatif tur (skor: {Score})",
                session.SessionId, state.ConsecutiveNegativeTurns, state.SentimentScore);
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
