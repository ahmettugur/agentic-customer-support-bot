// Application/Services/Realtime/VoiceTurnRecorder.cs
// Sesli turları geçmişe yazar ve duygu değerlendirmesini yapar — iki ses motoru da kullanır.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>Realtime ve Live motorlarının ortak tur kaydı: geçmiş, temsilci paneli, duygu ve eskalasyon.</summary>
internal sealed class VoiceTurnRecorder(
    IChatBridge chatBridge,
    ISessionManager sessionManager,
    ILogger logger,
    SessionStateService? sessionState = null,
    IEscalationSink? escalations = null)
{
    /// <summary>Sesli kanalın açtığı eskalasyonlarda ajan adı (session + ajan dedup anahtarı).</summary>
    internal const string VoiceAgentName = "RealtimeVoiceAgent";

    private readonly IChatBridge _chatBridge = chatBridge;
    private readonly ISessionManager _sessionManager = sessionManager;
    private readonly ILogger _logger = logger;
    private readonly SessionStateService? _sessionState = sessionState;
    private readonly IEscalationSink? _escalations = escalations;

    /// <summary>
    /// Eşleştirilmiş sesli turları geçmişe yazar: admin paneli için chat bridge'e, ajan bağlamı
    /// için oturum geçmişine (bu yazma olmadan sesli turlar konuşma geçmişinde hiç görünmüyordu)
    /// ve ardından duygu değerlendirmesi.
    /// </summary>
    public async Task RecordAsync(
        IBrowserChannel channel, AgentSession session, IReadOnlyList<VoiceTurn> ready, CancellationToken ct)
    {
        foreach (var turn in ready)
        {
            // Yanıtı gelmeden biten görüşmenin son cümlesi: soru kaybolmasın, ama boş bir bot mesajı da yazılmasın.
            if (turn.BotText.Length == 0)
            {
                await _sessionManager.AppendUserMessageAsync(session.SessionId, turn.UserSide, ct);
                continue;
            }

            await _chatBridge.RecordBotExchangeAsync(session.SessionId, turn.UserSide, turn.BotText);
            await _sessionManager.AddExchangeAsync(session.SessionId, turn.UserSide, turn.BotText, ct: ct);
            await EvaluateSentimentAsync(channel, session.SessionId, turn.UserSide, turn.BotText, ct);
        }
    }

    /// <summary>
    /// Metin kanalıyla eşdeğerlik: tur kaydedildikten sonra duygu durumu değerlendirilir
    /// (bkz. <c>ChatPortService.HandleStreamAsync</c> sonu) ve aynı olaylar yayınlanır.
    ///
    /// <para>
    /// Fark: metin kanalında eskalasyon, workflow'daki uzman ajanın "needs_escalation"
    /// kararından gelir. Sesli kanalda workflow yoktur — model tool'larla doğrudan konuşur —
    /// dolayısıyla uzman ajan kararı da yoktur. Müşteri açıkça isterse <c>human_handoff_tool</c>
    /// bir talep açar; istemeden art arda olumsuz turlar yaşayan müşteri için ise elimizdeki
    /// sinyal ardışık olumsuz duygudur — uyarı eşiği aşıldığında bir eskalasyon açılır. Sink'in
    /// session + ajan dedup'ı oturum ve ajan başına tek açık kayıt tutar.
    /// </para>
    /// </summary>
    private async Task EvaluateSentimentAsync(
        IBrowserChannel channel, string sessionId, string userSide, string botText, CancellationToken ct)
    {
        if (_sessionState is null) return;

        try
        {
            var current = await _sessionManager.GetAsync(sessionId, ct);
            if (current is null) return;

            var alert = _sessionState.CheckSentimentAlert(current);
            await channel.SendJsonAsync(new
            {
                type = StreamEventTypes.SentimentUpdate,
                data = new
                {
                    sentiment = alert.Sentiment,
                    score = alert.Score,
                    consecutive = alert.ConsecutiveNegativeTurns,
                    sessionId = alert.SessionId
                }
            }, ct);

            if (!alert.ShouldAlert) return;

            await channel.SendJsonAsync(new
            {
                type = StreamEventTypes.SentimentAlert,
                data = new
                {
                    sentiment = alert.Sentiment,
                    score = alert.Score,
                    consecutive = alert.ConsecutiveNegativeTurns,
                    sessionId = alert.SessionId,
                    message = alert.AlertMessage
                }
            }, ct);

            if (_escalations is null) return;

            var request = new EscalationRequest
            {
                SessionId = sessionId,
                AgentName = VoiceAgentName,
                UserQuery = userSide,
                Reason = $"Sesli görüşmede {alert.ConsecutiveNegativeTurns} ardışık olumsuz tur " +
                         $"(skor: {alert.Score:0.00}). {alert.AlertMessage}",
                ResponseSummary = botText.Length > 500 ? botText[..500] + "…" : botText,
                Priority = EscalationPriority.High
            };
            var stored = await _escalations.CreateAsync(request);
            if (stored.Id == request.Id)
            {
                _logger.LogWarning(
                    "Voice: ardışık olumsuz duygu — eskalasyon açıldı session={Sid} id={Id}",
                    sessionId, stored.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sesli görüşme, duygu değerlendirmesi/eskalasyon kaydı yüzünden kesilmemeli.
            _logger.LogWarning(ex, "Voice: duygu değerlendirmesi başarısız session={Sid}", sessionId);
        }
    }
}
