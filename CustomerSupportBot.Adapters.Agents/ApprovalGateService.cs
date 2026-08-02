// Adapters.Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.
//
// Onay bekletme mantığı artık framework'ün kendi mekanizmasına dayanıyor:
// yan etkili tool'lar ApprovalRequiredAIFunction ile sarmalanıyor,
// FunctionInvokingChatClient bu tool'lardan gelen çağrıyı gerçekten ÇALIŞTIRMADAN
// önce bir RequestInfoEvent olarak workflow superstep'ini duraklatıyor
// (bkz. WorkflowRunner.HandleApprovalRequestAsync — event'i yakalayıp bu servisteki
// RequestApprovalAsync ile aynı IApprovalQueue/SSE/SLA altyapısını tetikleyen taraf).
// Eskiden bu bekleme tool lambda'sının İÇİNDE (bloklayan bir await) yapılıyordu;
// artık workflow'un kendisi duraklatılıyor — checkpoint'lenebilir bir superstep durması,
// süreç-içi bir Task değil.

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
        var inner = AIFunctionFactory.Create(
            (
                [System.ComponentModel.Description("Sipariş verilecek ürünün adı")] string productName,
                [System.ComponentModel.Description("Sipariş adedi")] int? quantity,
                [System.ComponentModel.Description("Müşteri kimlik numarası (zorunlu)")] string customerId) =>
                _tools.OrderPlacementTool(productName, quantity, customerId),
            name: WellKnown.ToolNames.OrderPlacement,
            description:
                "Yeni sipariş oluşturur. Ürün adı, adet ve müşteri kimlik numarası zorunludur. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");

        return WrapIfRequiresApproval(WellKnown.ToolNames.OrderPlacement, inner);
    }

    public AIFunction BuildComplaintRegistrationTool()
    {
        var inner = AIFunctionFactory.Create(
            (
                [System.ComponentModel.Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu)")] string orderId,
                [System.ComponentModel.Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText,
                [System.ComponentModel.Description("Müşteri kimlik numarası (opsiyonel)")] string? customerId) =>
                _tools.ComplaintRegistrationTool(orderId, complaintText, customerId),
            name: WellKnown.ToolNames.ComplaintRegistration,
            description:
                "Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");

        return WrapIfRequiresApproval(WellKnown.ToolNames.ComplaintRegistration, inner);
    }

    public AIFunction BuildOrderCancelTool()
    {
        var inner = AIFunctionFactory.Create(
            (
                [System.ComponentModel.Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')")] string orderId,
                [System.ComponentModel.Description("İptal sebebi (zorunlu, en az 5 karakter)")] string reason) =>
                _tools.OrderCancelTool(orderId, reason),
            name: WellKnown.ToolNames.OrderCancel,
            description:
                "Mevcut bir siparişi iptal eder. Sadece 'İşleniyor' veya 'Kargolandı' durumundaki siparişler iptal edilebilir. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");

        return WrapIfRequiresApproval(WellKnown.ToolNames.OrderCancel, inner);
    }

    public AIFunction BuildReturnRequestTool()
    {
        var inner = AIFunctionFactory.Create(
            (
                [System.ComponentModel.Description("İade talep edilecek sipariş numarası (zorunlu, ör. '1042')")] string orderId,
                [System.ComponentModel.Description("İade sebebi (zorunlu, en az 5 karakter)")] string reason) =>
                _tools.ReturnRequestTool(orderId, reason),
            name: WellKnown.ToolNames.ReturnRequest,
            description:
                "Teslim edilmiş bir sipariş için iade talebi oluşturur. Sadece 'Teslim Edildi' durumundaki " +
                "ve 14 gün içindeki siparişler iade edilebilir. " +
                "Bu tool HITL approval gate'inden geçer — admin onayı bekler.");

        return WrapIfRequiresApproval(WellKnown.ToolNames.ReturnRequest, inner);
    }

    /// <summary>
    /// Tool'u sadece config'de onay gerektiriyorsa <see cref="ApprovalRequiredAIFunction"/> ile
    /// sarmalar — FunctionInvokingChatClient bu işaretli tool'ları gerçekten çağırmadan önce
    /// bir <c>ToolApprovalRequestContent</c> üretip workflow'un duraklamasını sağlıyor.
    /// Onay kapalıysa (<see cref="ApprovalOptions.Enabled"/>=false) veya bu tool listede yoksa
    /// eski davranış korunur: tool doğrudan çalışır.
    /// </summary>
    private AIFunction WrapIfRequiresApproval(string toolName, AIFunction inner) =>
        RequiresApproval(toolName) ? new ApprovalRequiredAIFunction(inner) : inner;

    private bool RequiresApproval(string toolName) =>
        _approvalOptions.Enabled && _approvalOptions.ToolsRequiringApproval.Contains(toolName);

    /// <summary>
    /// Hangi ajanın hangi tool'u sahiplendiğini çözer — <c>ToolApprovalRequestContent</c>
    /// sadece tool adını taşıdığı için (workflow hangi ajanın turduğunu doğrudan söylemiyor),
    /// bu eşleme admin panelinde "hangi ajan istiyor" bilgisini göstermek için gerekiyor.
    /// Eşleme <see cref="WellKnown.SideEffectToolOwners"/>'dan okunur — burada ayrıca
    /// elle sürdürülen bir switch tutulmaz.
    /// </summary>
    public static string ResolveAgentName(string toolName) =>
        WellKnown.SideEffectToolOwners.TryGetValue(toolName, out var agentName)
            ? agentName
            : "UnknownAgent";

    /// <summary>
    /// Bir onay talebi oluşturur (veya aynı imzalı bekleyen bir talep varsa onu yeniden kullanır)
    /// ve admin kararını bekler. WorkflowRunner, framework'ün <c>RequestInfoEvent</c>'ini
    /// yakaladığında bu metodu çağırır — mevcut IApprovalQueue/SSE/SLA altyapısı (bu metodun
    /// gövdesi) değişmedi, sadece ÇAĞRILDIĞI YER değişti: eskiden tool lambda'sının içinden,
    /// şimdi WorkflowRunner'ın workflow event döngüsünden.
    /// </summary>
    public async Task<ApprovalDecisionResult> RequestApprovalAsync(
        string toolName,
        string agentName,
        IDictionary<string, object?>? parameters,
        string? justification,
        CancellationToken ct)
    {
        var paramsDict = parameters is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(parameters);

        var ctx = _contextAccessor.Context;

        var paramSig = BuildParamSignature(paramsDict);
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
                Parameters = paramsDict,
                // Çağıran taraf (WorkflowRunner) o an trace'te bulunan gerçek gerekçeyi
                // (PlanningAgent rationale'ı) geçer; yoksa jenerik şablona düşülür.
                Justification = string.IsNullOrWhiteSpace(justification)
                    ? string.Format(WellKnown.ApprovalReasons.AgentWantsToCall, agentName)
                    : justification
            };
            await _approvalQueue.CreateAsync(req, ct);
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
