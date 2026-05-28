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
    public void ProcessPendingEscalations(ReasoningTrace trace, string userQuery, string finalResponse)
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

        // Session bazlı dedup — aynı session'da açık eskalasyon varsa yeni oluşturma.
        if (!string.IsNullOrWhiteSpace(trace.SessionId))
        {
            var existing = _escalationSink.GetOpen()
                .FirstOrDefault(e => string.Equals(e.SessionId, trace.SessionId, StringComparison.Ordinal));

            if (existing != null)
            {
                _logger.LogDebug(
                    "[HITL] Session {SessionId} için zaten açık eskalasyon var (id={Id}, status={Status}); yeni kayıt oluşturulmuyor.",
                    trace.SessionId, existing.Id, existing.Status);
                return;
            }
        }

        foreach (var sr in candidates)
        {
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

                ApplyRoutingDecision(newRequest, trace, sr.AgentName);
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

    private void ApplyRoutingDecision(
        EscalationRequest req,
        ReasoningTrace trace,
        string? agentName)
    {
        if (_router == null) return;
        try
        {
            CustomerProfile? profile = null;
            string? customerId = null;
            if (!string.IsNullOrWhiteSpace(trace.SessionId) && _sessionManager != null)
            {
                customerId = _sessionManager.Get(trace.SessionId!)?.State.CustomerId;
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
