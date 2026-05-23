// Application/Services/ReplanService.cs
// IReplanService implementasyonu: bir session'daki son kullanıcı mesajını yeniden
// değerlendirerek bot yanıtı üretir ve bridge aracılığıyla müşteriye iletir.

using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Replan use case implementasyonu.
/// Reasoning + workflow pipeline'ı koşturur, yanıtı session history'ye yazar
/// ve ChatBridge üzerinden müşteriye bot mesajı olarak yayınlar.
/// </summary>
public sealed class ReplanService : IReplanService
{
    private readonly ISessionManager _sessions;
    private readonly IChatBridge _bridge;
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoning;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly ILogger<ReplanService> _logger;

    public ReplanService(
        ISessionManager sessions,
        IChatBridge bridge,
        IAgentTeamPort team,
        IReasoningPort reasoning,
        IApprovalContextAccessor approvalContext,
        ILogger<ReplanService> logger)
    {
        _sessions = sessions;
        _bridge = bridge;
        _team = team;
        _reasoning = reasoning;
        _approvalContext = approvalContext;
        _logger = logger;
    }

    public async Task ExecuteAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var session = _sessions.Get(sessionId);
            if (session == null) return;

            var history = _sessions.GetHistory(sessionId);
            var lastUserQuery = history.LastOrDefault(m => m.Role == ConversationRoles.User)?.Text;

            // Admin notu varsa onu öncelikli query olarak kullan — Reasoning/Planning
            // agent'lar gerçek müşteri talebi olarak admin notunu işler. Eski mesaj
            // history'de bağlam için kalır.
            var effectiveQuery = !string.IsNullOrWhiteSpace(session.State.ReplanNote)
                ? session.State.ReplanNote!
                : lastUserQuery;

            if (string.IsNullOrWhiteSpace(effectiveQuery))
                return;

            _bridge.PublishBotTyping(sessionId, true);

            try
            {
                var reasoningResult = await _reasoning.ReasonAsync(effectiveQuery, session, history, ct);

                using var approvalScope = _approvalContext.SetScope(sessionId, null, effectiveQuery);
                var response = await _team.RunAsync(effectiveQuery, history, session, reasoningResult);

                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("Replan bot run produced empty response for session {Session}", sessionId);
                    return;
                }

                _sessions.AppendAssistantMessage(sessionId, response);
                _bridge.PublishBotMessage(sessionId, response);
            }
            finally
            {
                _bridge.PublishBotTyping(sessionId, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replan bot run failed for session {Session}", sessionId);
            try
            {
                _bridge.PublishSystemMessage(sessionId,
                    "⚠️ Otomatik yeniden planlama sırasında bir sorun oluştu. " +
                    "Lütfen sorunuzu tekrar yazar mısınız?");
            }
            catch { /* swallow */ }
        }
    }
}
