// Adapters.Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Escalation;
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

    public AIFunction BuildOrderCancelTool()
    {
        return AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')")] string orderId,
                [System.ComponentModel.Description("İptal sebebi (zorunlu, en az 5 karakter)")] string reason,
                CancellationToken ct) =>
            {
                var decision = await RequestApprovalAsync(
                    toolName: WellKnown.ToolNames.OrderCancel,
                    agentName: WellKnown.AgentNames.Order,
                    parameters: new Dictionary<string, object?>
                    {
                        ["orderId"] = orderId,
                        ["reason"] = reason
                    },
                    ct);

                if (decision is { Approved: false } d)
                {
                    return ToolResult.ValidationError(
                        $"{WellKnown.FallbackMessages.ApprovalRejected}: {d.Reason ?? WellKnown.ApprovalReasons.AdminRejected}");
                }

                return _tools.OrderCancelTool(orderId, reason);
            },
            name: WellKnown.ToolNames.OrderCancel,
            description:
                "Mevcut bir siparişi iptal eder. Sadece 'İşleniyor' veya 'Kargolandı' durumundaki siparişler iptal edilebilir. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");
    }

    public AIFunction BuildReturnRequestTool()
    {
        return AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("İade talep edilecek sipariş numarası (zorunlu, ör. '1042')")] string orderId,
                [System.ComponentModel.Description("İade sebebi (zorunlu, en az 5 karakter)")] string reason,
                CancellationToken ct) =>
            {
                var decision = await RequestApprovalAsync(
                    toolName: WellKnown.ToolNames.ReturnRequest,
                    agentName: WellKnown.AgentNames.Order,
                    parameters: new Dictionary<string, object?>
                    {
                        ["orderId"] = orderId,
                        ["reason"] = reason
                    },
                    ct);

                if (decision is { Approved: false } d)
                {
                    return ToolResult.ValidationError(
                        $"{WellKnown.FallbackMessages.ApprovalRejected}: {d.Reason ?? WellKnown.ApprovalReasons.AdminRejected}");
                }

                return _tools.ReturnRequestTool(orderId, reason);
            },
            name: WellKnown.ToolNames.ReturnRequest,
            description:
                "Teslim edilmiş bir sipariş için iade talebi oluşturur. Sadece 'Teslim Edildi' durumundaki " +
                "ve 14 gün içindeki siparişler iade edilebilir. " +
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
