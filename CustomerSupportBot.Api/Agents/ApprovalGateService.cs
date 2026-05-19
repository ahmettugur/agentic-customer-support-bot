using CustomerSupportBot.Domain.Model.Memory;
// Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.
// Yan etkili tool çağrıları (OrderPlacement, ComplaintRegistration) öncesi
// admin onayı bekler. Workflow sonunda needs_escalation status'u olan
// specialist reasoning'leri escalation sink'e yazar.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.Services.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Agents;

/// <summary>
/// HITL approval gate ve escalation servislerini yönetir.
/// Tool çağrılarını approval gate ile sarar ve workflow sonrası
/// escalation tespiti yapar.
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

    /// <summary>
    /// OrderPlacementTool'u approval gate ile saran AIFunction builder.
    /// </summary>
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

    /// <summary>ComplaintRegistrationTool için approval gate wrapper.</summary>
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

    /// <summary>
    /// Approval queue'ya request yazar, admin kararını bekler, sonucu döner.
    /// HITL kapalıysa anında resolve olur (bypass).
    /// </summary>
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

        // ─── Idempotency: aynı session + tool + parametreler için zaten Pending bir
        // approval varsa onu reuse et. ChatManager'ın bir specialist'i aynı turda iki
        // kez seçmesi veya MAF function calling retry'ı, kuyrukta çift kayıt üretmesin.
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

    /// <summary>
    /// Trace'deki specialist reasoning'lerde needs_escalation status'u olanları
    /// tarar ve IEscalationSink'e yazar. Feature flag kapalıysa no-op.
    ///
    /// Çift kayıt önleme — iki katmanlı dedup:
    /// 1. <b>Trace içi</b>: Aynı agent için birden fazla needs_escalation reasoning
    ///    varsa (workflow tekrarı veya MAF output event tekrarı) sadece sonuncusu işlenir.
    /// 2. <b>Session içi</b>: Bu session için zaten Open/Acknowledged eskalasyon varsa
    ///    yeni kayıt oluşturulmaz — kullanıcı aynı session'da tekrar "temsilciye bağla"
    ///    derse mevcut eskalasyon kullanılır.
    /// </summary>
    public void ProcessPendingEscalations(ReasoningTrace trace, string userQuery, string finalResponse)
    {
        if (!_approvalOptions.EscalationEnabled) return;

        // ─── Katman 1: Trace içi dedup ───
        // AgentName bazında grupla, her grubun sonuncusunu al (sonraki reasoning öncekini supersede eder).
        var candidates = trace.SpecialistReasonings
            .Where(sr => sr.PostToolReflection?.StatusEnum == TaskCompletionStatus.NeedsEscalation)
            .GroupBy(sr => sr.AgentName ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (candidates.Count == 0) return;

        // ─── Katman 1.5: Session başına tek eskalasyon kuralı ───
        // Birden fazla ajan aynı turda needs_escalation dönerse (ör. Complaint +
        // HumanHandoff) admin paneli session bazında tek kayıt bekler. Complaint en
        // yüksek önceliği alır; yoksa son gelen kullanılır.
        if (candidates.Count > 1)
        {
            candidates = new List<SpecialistReasoning>
            {
                candidates.FirstOrDefault(c =>
                    string.Equals(c.AgentName, WellKnown.AgentNames.Complaint, StringComparison.OrdinalIgnoreCase))
                ?? candidates[^1]
            };
        }

        // ─── Katman 2: Session içi dedup ───
        // Bu session için zaten açık eskalasyon varsa skip — duplicate önler.
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

        // ─── Yeni eskalasyon(lar) oluştur ───
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

                // ─── Smart Routing & Skills-Based Escalation (#11) ───
                ApplyRoutingDecisionSafe(newRequest, trace, sr.AgentName);

                _escalationSink.Create(newRequest);

                // Atanan temsilcinin yükünü +1 yap (varsa)
                if (!string.IsNullOrWhiteSpace(newRequest.SuggestedAgentId))
                    _agentRegistry?.IncrementLoad(newRequest.SuggestedAgentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HITL] Escalation sink kaydı başarısız.");
            }
        }
    }

    /// <summary>
    /// (session, tool) tuple'ı altında parametre sözlüğünü deterministik bir imzaya çevirir.
    /// Approval queue'de çift kayıt önlemek için mevcut Pending request'lerle kıyaslanır.
    /// </summary>
    private static string BuildParamSignature(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return string.Empty;
        return string.Join("|", parameters
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? string.Empty}"));
    }

    /// <summary>
    /// Router enjekteyse skill çıkarımı + en iyi temsilci atama yapılır.
    /// Hata durumunda eskalasyon kaydı bozulmaz — routing alanları boş kalır.
    /// </summary>
    private void ApplyRoutingDecisionSafe(
        EscalationRequest req,
        ReasoningTrace trace,
        string? agentName)
    {
        if (_router == null) return;
        try
        {
            // Müşteri profili — varsa skill çıkarımına dahil edilir.
            CustomerProfile? profile = null;
            string? customerId = null;
            if (!string.IsNullOrWhiteSpace(trace.SessionId) && _sessionManager != null)
            {
                customerId = _sessionManager.GetSession(trace.SessionId!)?.State.CustomerId;
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

            // Öncelik — şikayet ve düşük match score'da yükselt
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

/// <summary>Approval karar sonucu.</summary>
public sealed record ApprovalDecisionResult(bool Approved, string? Reason);

