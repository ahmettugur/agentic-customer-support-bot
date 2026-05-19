// Adapters.Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// HITL approval gate ve escalation servislerini yönetir.
/// </summary>
public class ApprovalGateService
{
    private readonly IApprovalQueue _approvalQueue;
    private readonly ApprovalOptions _approvalOptions;
    private readonly IEscalationSink _escalationSink;
    private readonly IApprovalContextAccessor _contextAccessor;
    private readonly CustomerSupportToolsService _tools;
    private readonly ISkillsBasedRouter? _router;
    private readonly IHumanAgentRegistry? _agentRegistry;
    private readonly ICustomerProfileStore? _profileStore;
    private readonly ISessionManager? _sessionManager;
    private readonly ILogger<ApprovalGateService> _logger;

    public ApprovalGateService(
        IApprovalQueue approvalQueue,
        IOptions<ApprovalOptions> approvalOptions,
        IEscalationSink escalationSink,
        IApprovalContextAccessor contextAccessor,
        CustomerSupportToolsService tools,
        ISkillsBasedRouter? router = null,
        IHumanAgentRegistry? agentRegistry = null,
        ICustomerProfileStore? profileStore = null,
        ISessionManager? sessionManager = null,
        ILogger<ApprovalGateService>? logger = null)
    {
        _approvalQueue = approvalQueue;
        _approvalOptions = approvalOptions.Value;
        _escalationSink = escalationSink;
        _contextAccessor = contextAccessor;
        _tools = tools;
        _router = router;
        _agentRegistry = agentRegistry;
        _profileStore = profileStore;
        _sessionManager = sessionManager;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ApprovalGateService>.Instance;
    }

    public AIFunction BuildOrderPlacementTool()
    {
        return AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Sipariş verilecek ürünün adı")] string productName,
                [System.ComponentModel.Description("Sipariş adedi")] int? quantity,
                [System.ComponentModel.Description("Müşteri kimlik numarası (zorunlu)")] string customerId,
                CancellationToken ct) =>
            {
                var decision = await RequestApprovalAsync(
                    toolName: WellKnown.ToolNames.OrderPlacement,
                    agentName: WellKnown.AgentNames.Order,
                    parameters: new Dictionary<string, object?>
                    {
                        ["productName"] = productName,
                        ["quantity"] = quantity,
                        ["customerId"] = customerId
                    },
                    ct);

                if (decision is { Approved: false } d)
                {
                    return ToolResult.ValidationError(
                        $"{WellKnown.FallbackMessages.ApprovalRejected}: {d.Reason ?? WellKnown.ApprovalReasons.AdminRejected}");
                }

                return _tools.OrderPlacementTool(productName, quantity, customerId);
            },
            name: WellKnown.ToolNames.OrderPlacement,
            description:
                "Yeni sipariş oluşturur. Ürün adı, adet ve müşteri kimlik numarası zorunludur. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");
    }

    public AIFunction BuildComplaintRegistrationTool()
    {
        return AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu)")] string orderId,
                [System.ComponentModel.Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText,
                [System.ComponentModel.Description("Müşteri kimlik numarası (opsiyonel)")] string? customerId,
                CancellationToken ct) =>
            {
                var decision = await RequestApprovalAsync(
                    toolName: WellKnown.ToolNames.ComplaintRegistration,
                    agentName: WellKnown.AgentNames.Complaint,
                    parameters: new Dictionary<string, object?>
                    {
                        ["orderId"] = orderId,
                        ["complaintText"] = complaintText,
                        ["customerId"] = customerId
                    },
                    ct);

                if (decision is { Approved: false } d)
                {
                    return ToolResult.ValidationError(
                        $"{WellKnown.FallbackMessages.ComplaintRejected}: {d.Reason ?? WellKnown.ApprovalReasons.AdminRejected}");
                }

                return _tools.ComplaintRegistrationTool(orderId, complaintText, customerId);
            },
            name: WellKnown.ToolNames.ComplaintRegistration,
            description:
                "Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");
    }

    private async Task<ApprovalDecisionResult> RequestApprovalAsync(
        string toolName,
        string agentName,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        if (!_approvalOptions.Enabled
            || !_approvalOptions.ToolsRequiringApproval.Contains(toolName))
        {
            return new ApprovalDecisionResult(Approved: true, Reason: null);
        }

        var ctx = _contextAccessor.Context;

        var paramSig = BuildParamSignature(parameters);
        var existing = ctx?.SessionId is { Length: > 0 } sid
            ? _approvalQueue.GetPending().FirstOrDefault(p =>
                  string.Equals(p.SessionId, sid, StringComparison.Ordinal)
               && string.Equals(p.ToolName, toolName, StringComparison.Ordinal)
               && string.Equals(BuildParamSignature(p.Parameters), paramSig, StringComparison.Ordinal))
            : null;

        ApprovalRequest req;
        if (existing != null)
        {
            req = existing;
        }
        else
        {
            req = new ApprovalRequest
            {
                SessionId = ctx?.SessionId,
                TraceId = ctx?.TraceId,
                UserQuery = ctx?.UserQuery,
                ToolName = toolName,
                AgentName = agentName,
                Parameters = parameters,
                Justification = string.Format(WellKnown.ApprovalReasons.AgentWantsToCall, agentName)
            };
            _approvalQueue.Create(req);
        }

        try
        {
            var resolved = await _approvalQueue.AwaitDecisionAsync(req.Id, ct);
            var approved = resolved.Status == ApprovalStatus.Approved;
            return new ApprovalDecisionResult(approved, resolved.DecisionReason);
        }
        catch (OperationCanceledException)
        {
            return new ApprovalDecisionResult(false, WellKnown.FallbackMessages.RequestCancelled);
        }
    }

    public void ProcessPendingEscalations(ReasoningTrace trace, string userQuery, string finalResponse)
    {
        if (!_approvalOptions.EscalationEnabled) return;

        var candidates = trace.SpecialistReasonings
            .Where(sr => sr.PostToolReflection?.StatusEnum == TaskCompletionStatus.NeedsEscalation)
            .GroupBy(sr => sr.AgentName ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (candidates.Count == 0) return;

        if (candidates.Count > 1)
        {
            candidates = new List<SpecialistReasoning>
            {
                candidates.FirstOrDefault(c =>
                    string.Equals(c.AgentName, WellKnown.AgentNames.Complaint, StringComparison.OrdinalIgnoreCase))
                ?? candidates[^1]
            };
        }

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

                ApplyRoutingDecisionSafe(newRequest, trace, sr.AgentName);
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

    private static string BuildParamSignature(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return string.Empty;
        return string.Join("|", parameters
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? string.Empty}"));
    }

    private void ApplyRoutingDecisionSafe(
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

public sealed record ApprovalDecisionResult(bool Approved, string? Reason);
