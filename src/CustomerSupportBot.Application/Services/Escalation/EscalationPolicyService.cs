using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Escalation;

/// <summary>
/// Eskalasyon iş politikası servisi.
/// Dedup, öncelik yükseltme ve skills-based routing kararlarını içerir.
/// Adapter'dan bağımsız, Application katmanında konumlanır.
/// </summary>
public class EscalationPolicyService
{
    private readonly IEscalationSink _escalationSink;
    private readonly ISkillsBasedRouter? _router;
    private readonly IHumanAgentRegistry? _agentRegistry;
    private readonly ICustomerProfileStore? _profileStore;
    private readonly ISessionManager? _sessionManager;
    private readonly ApprovalOptions _options;
    private readonly ILogger<EscalationPolicyService> _logger;

    public EscalationPolicyService(
        IEscalationSink escalationSink,
        IOptions<ApprovalOptions> options,
        ISkillsBasedRouter? router = null,
        IHumanAgentRegistry? agentRegistry = null,
        ICustomerProfileStore? profileStore = null,
        ISessionManager? sessionManager = null,
        ILogger<EscalationPolicyService>? logger = null)
    {
        _escalationSink = escalationSink;
        _options = options.Value;
        _router = router;
        _agentRegistry = agentRegistry;
        _profileStore = profileStore;
        _sessionManager = sessionManager;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EscalationPolicyService>.Instance;
    }

    /// <summary>
    /// Reasoning trace'ten eskalasyon adaylarını belirler, dedup uygular,
    /// routing kararı alır ve eskalasyon kaydı oluşturur.
    /// </summary>
    public async Task ProcessPendingEscalationsAsync(
        ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default)
    {
        if (!_options.EscalationEnabled) return;

        var candidates = trace.SpecialistReasonings
            .Where(sr => sr.PostToolReflection?.StatusEnum == TaskCompletionStatus.NeedsEscalation)
            .GroupBy(sr => sr.AgentName ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (candidates.Count == 0) return;

        // Birden fazla aday varsa Complaint öncelikli, yoksa son aday.
        if (candidates.Count > 1)
        {
            candidates = new List<SpecialistReasoning>
            {
                candidates.FirstOrDefault(c =>
                    string.Equals(c.AgentName, WellKnown.AgentNames.Complaint, StringComparison.OrdinalIgnoreCase))
                ?? candidates[^1]
            };
        }

        foreach (var sr in candidates)
        {
            // Session + ajan bazlı dedup — aynı session'da AYNI ajandan zaten açık bir
            // eskalasyon varsa yeni oluşturma. Sadece SessionId'ye bakmak (eski davranış)
            // aynı sohbette farklı bir ajandan gelen, tamamen bağımsız bir eskalasyonu da
            // bastırıyordu (ör. Order eskalasyonu açıkken Complaint eskalasyonu hiç açılmıyordu).
            if (!string.IsNullOrWhiteSpace(trace.SessionId))
            {
                var existing = _escalationSink.GetOpen().FirstOrDefault(e =>
                    string.Equals(e.SessionId, trace.SessionId, StringComparison.Ordinal)
                    && string.Equals(e.AgentName, sr.AgentName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    _logger.LogDebug(
                        "[HITL] Session {SessionId} için {AgentName} ajanından zaten açık eskalasyon var " +
                        "(id={Id}, status={Status}); yeni kayıt oluşturulmuyor.",
                        trace.SessionId, sr.AgentName, existing.Id, existing.Status);
                    continue;
                }
            }

            var reflection = sr.PostToolReflection!;
            try
            {
                var newRequest = new EscalationRequest
                {
                    SessionId = trace.SessionId,
                    TraceId = trace.TraceId,
                    AgentName = sr.AgentName,
                    UserQuery = userQuery,
                    Reason = string.IsNullOrWhiteSpace(reflection.HandoffReason)
                        ? reflection.Summary
                        : reflection.HandoffReason,
                    MissingContext = reflection.MissingContext,
                    ResponseSummary = finalResponse.Length > 500
                        ? finalResponse[..500] + "…"
                        : finalResponse
                };

                await ApplyRoutingDecisionAsync(newRequest, trace, sr.AgentName, ct);
                _escalationSink.Create(newRequest);

                if (!string.IsNullOrWhiteSpace(newRequest.SuggestedAgentId))
                    _agentRegistry?.IncrementLoad(newRequest.SuggestedAgentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HITL] Escalation sink kaydı başarısız.");
            }
        }
    }

    private async Task ApplyRoutingDecisionAsync(
        EscalationRequest req,
        ReasoningTrace trace,
        string? agentName,
        CancellationToken ct)
    {
        if (_router == null) return;
        try
        {
            CustomerProfile? profile = null;
            string? customerId = null;
            if (!string.IsNullOrWhiteSpace(trace.SessionId) && _sessionManager != null)
            {
                var session = await _sessionManager.GetAsync(trace.SessionId!, ct);
                // AuthenticatedCustomerId (JWT) — State.CustomerId DEĞİL: eskalasyon
                // yönlendirmesi başkasının profiline göre yapılmamalı.
                customerId = session?.State.AuthenticatedCustomerId;
            }
            if (!string.IsNullOrWhiteSpace(customerId) && _profileStore != null)
            {
                profile = _profileStore.Get(customerId!);
            }

            var decision = _router.Decide(trace, agentName, profile);
            req.RequiredSkills = decision.MatchedSkills.Concat(decision.MissingSkills)
                                                       .Distinct(StringComparer.Ordinal)
                                                       .ToList();
            req.SuggestedAgentId = decision.SuggestedAgentId;
            req.SuggestedAgentName = decision.SuggestedAgentName;
            req.MatchScore = decision.MatchScore;
            req.RoutingNote = decision.Note;

            // Öncelik yükseltme kuralları
            if (string.Equals(agentName, WellKnown.AgentNames.Complaint, StringComparison.OrdinalIgnoreCase))
                req.Priority = EscalationPriority.High;
            else if (decision.MatchScore < 0.3 && req.Priority < EscalationPriority.High)
                req.Priority = EscalationPriority.High;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] Skills-based routing kararı başarısız.");
        }
    }
}
