// Adapters.Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
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
    private readonly ICustomerSupportToolsService _tools;
    private readonly EscalationPolicyService _escalationPolicy;
    private readonly ILogger<ApprovalGateService> _logger;

    public ApprovalGateService(
        IApprovalQueue approvalQueue,
        IOptions<ApprovalOptions> approvalOptions,
        IEscalationSink escalationSink,
        IApprovalContextAccessor contextAccessor,
        ICustomerSupportToolsService tools,
        EscalationPolicyService escalationPolicy,
        ILogger<ApprovalGateService>? logger = null)
    {
        _approvalQueue = approvalQueue;
        _approvalOptions = approvalOptions.Value;
        _escalationSink = escalationSink;
        _contextAccessor = contextAccessor;
        _tools = tools;
        _escalationPolicy = escalationPolicy;
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
        _escalationPolicy.ProcessPendingEscalations(trace, userQuery, finalResponse);
    }

    private static string BuildParamSignature(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return string.Empty;
        return string.Join("|", parameters
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? string.Empty}"));
    }

}

public sealed record ApprovalDecisionResult(bool Approved, string? Reason);
