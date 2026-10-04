// Adapters.Agents/ApprovalGateService.cs
// HITL — Human-in-the-Loop approval gate + escalation sink servisleri.
//
// ONAY ARTIK BLOKLAMIYOR. Yan etkili 4 tool (sipariş/iptal/iade/şikayet) admin kararını
// HİÇBİR YERDE beklemez: ExecuteWithApprovalGateAsync onay kaydını oluşturup hemen
// ToolResult.Pending döner ve tur biter. Gerçek iş, admin karar verdiğinde
// IApprovalExecutionRouter üzerinden ayrıca tetiklenir; sonuç kullanıcıya bildirim
// olarak ulaşır.
//
// Bundan önce iki bekleyen model denendi ve ikisi de aynı sebepten terk edildi —
// kullanıcının turu bir insanın ne zaman karar vereceğine bağlıydı, onaylar birikince
// TimeoutSeconds içinde yetişilemiyor ve istekler sessizce otomatik red'e düşüyordu:
//   1) bloklayan await, tool lambda'sının içinde;
//   2) ApprovalRequiredAIFunction + RequestInfoEvent ile workflow superstep duraklaması.
//
// (2)'nin köprüsü (WorkflowRunner.HandleRequestInfoEventAsync + aşağıdaki
// RequestApprovalAsync) kodda DURUYOR ama bu 4 tool için hiç tetiklenmiyor — hiçbiri
// artık ApprovalRequiredAIFunction ile sarılmıyor, dolayısıyla o event'i üretmiyorlar.
// Köprü, ileride biri bilerek bir tool'u o modelde sararsa çalışsın diye korunuyor.

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
    private readonly IEscalationSink _escalationSink;
    private readonly IApprovalContextAccessor _contextAccessor;
    private readonly ICustomerSupportToolsService _tools;
    private readonly EscalationPolicyService _escalationPolicy;
    private readonly SideEffectApprovalGate _gate;
    private readonly ILogger<ApprovalGateService> _logger;

    public ApprovalGateService(
        IApprovalQueue approvalQueue,
        IOptions<ApprovalOptions> approvalOptions,
        IEscalationSink escalationSink,
        IApprovalContextAccessor contextAccessor,
        ICustomerSupportToolsService tools,
        EscalationPolicyService escalationPolicy,
        ILogger<ApprovalGateService>? logger = null,
        IAttachmentStore? attachments = null)
    {
        _approvalQueue = approvalQueue;
        _escalationSink = escalationSink;
        _contextAccessor = contextAccessor;
        _tools = tools;
        _escalationPolicy = escalationPolicy;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ApprovalGateService>.Instance;
        // Kapı kuralı tek yerde (Application) — sesli kanal da aynı sınıfı kullanır.
        _gate = new SideEffectApprovalGate(approvalQueue, approvalOptions, _logger, attachments);
    }

    /// <summary>
    /// customerId artık LLM'e sorulan bir parametre DEĞİL — kullanıcı metninde başka bir
    /// müşteri numarası söylese bile bu tool'lar her zaman login'li kullanıcının doğrulanmış
    /// kimliğini (<see cref="IApprovalContextAccessor"/> → JWT claim) kullanır. Bu, eskiden
    /// var olan "kullanıcı başkasının müşteri numarasını söyleyip işlem yaptırabilir" açığını kapatır.
    /// </summary>
    private string CurrentCustomerId => _contextAccessor.Context?.CustomerId ?? "";

    /// <summary>
    /// Çok ürünlü sipariş: LLM tek çağrıda birden fazla satır gönderir.
    ///
    /// <para>
    /// Alternatif — her ürün için ayrı bir tool çağrısı — kasıtlı olarak seçilmedi: her çağrı
    /// AYRI bir onay kaydı üretirdi, admin bunları tek tek onaylardı ve biri onaylanıp diğeri
    /// reddedilerek yarım bir sepet oluşabilirdi. Tek çağrı = tek onay = tek sipariş.
    /// </para>
    /// </summary>
    public AIFunction BuildOrderPlacementTool() =>
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description(
                    "Sipariş satırları. Her satır bir ürün adı ve adet içerir; kullanıcı birden " +
                    "fazla ürün istediyse HEPSİNİ tek listede gönder.")]
                OrderLineRequest[] lines) =>
                await ExecuteWithApprovalGateAsync(
                    WellKnown.ToolNames.OrderPlacement,
                    // customerId ayrıca yazılır: onay kaydı Postgres'ten hydrate edilirken
                    // ApprovalExecutionRouter satırları buradan okur (bkz. ParametersJson).
                    new Dictionary<string, object?> { ["lines"] = lines, ["customerId"] = CurrentCustomerId },
                    () => _tools.OrderPlacementTool(lines, CurrentCustomerId)),
            name: WellKnown.ToolNames.OrderPlacement,
            description:
                "Yeni sipariş oluşturur. Tek siparişte birden fazla ürün satırı olabilir; müşteri kimliği " +
                "login'den otomatik alınır. Bu tool HITL approval gate'inden geçer — admin onaya gönderilir, " +
                "sonucu bildirim olarak dönülür.");

    public AIFunction BuildComplaintRegistrationTool() =>
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu)")] string orderId,
                [System.ComponentModel.Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText) =>
                await ExecuteWithApprovalGateAsync(
                    WellKnown.ToolNames.ComplaintRegistration,
                    new Dictionary<string, object?> { ["orderId"] = orderId, ["complaintText"] = complaintText, ["customerId"] = CurrentCustomerId },
                    () => _tools.ComplaintRegistrationTool(orderId, complaintText, CurrentCustomerId),
                    preflight: () => _tools.ValidateOrderActionable(orderId, CurrentCustomerId)),
            name: WellKnown.ToolNames.ComplaintRegistration,
            description:
                "Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur; müşteri kimliği " +
                "login'den otomatik alınır. Bu tool HITL approval gate'inden geçer — admin onaya gönderilir, sonucu bildirim olarak dönülür.");

    public AIFunction BuildOrderCancelTool() =>
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("İptal edilecek sipariş numarası (zorunlu, ör. '1030')")] string orderId,
                [System.ComponentModel.Description("İptal sebebi (zorunlu, en az 5 karakter)")] string reason) =>
                await ExecuteWithApprovalGateAsync(
                    WellKnown.ToolNames.OrderCancel,
                    new Dictionary<string, object?> { ["orderId"] = orderId, ["reason"] = reason, ["customerId"] = CurrentCustomerId },
                    () => _tools.OrderCancelTool(orderId, reason, CurrentCustomerId),
                    preflight: () => _tools.ValidateOrderActionable(orderId, CurrentCustomerId)),
            name: WellKnown.ToolNames.OrderCancel,
            description:
                "Mevcut bir siparişi iptal eder. Sadece 'İşleniyor' veya 'Kargolandı' durumundaki siparişler iptal edilebilir; " +
                "sadece login'li müşterinin kendi siparişleri iptal edilebilir, müşteri kimliği login'den otomatik alınır. " +
                "Bu tool HITL approval gate'inden geçer — admin onaya gönderilir, sonucu bildirim olarak dönülür.");

    public AIFunction BuildReturnRequestTool() =>
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("İade talep edilecek sipariş numarası (zorunlu, ör. '1042')")] string orderId,
                [System.ComponentModel.Description("İade sebebi (zorunlu, en az 5 karakter)")] string reason) =>
                await ExecuteWithApprovalGateAsync(
                    WellKnown.ToolNames.ReturnRequest,
                    new Dictionary<string, object?> { ["orderId"] = orderId, ["reason"] = reason, ["customerId"] = CurrentCustomerId },
                    () => _tools.ReturnRequestTool(orderId, reason, CurrentCustomerId),
                    preflight: () => _tools.ValidateOrderActionable(orderId, CurrentCustomerId)),
            name: WellKnown.ToolNames.ReturnRequest,
            description:
                "Teslim edilmiş bir sipariş için iade talebi oluşturur. Sadece 'Teslim Edildi' durumundaki " +
                "ve teslimden itibaren 14 gün içindeki siparişler iade edilebilir; sadece login'li müşterinin kendi siparişleri iade " +
                "edilebilir, müşteri kimliği login'den otomatik alınır. " +
                "Bu tool HITL approval gate'inden geçer — admin onaya gönderilir, sonucu bildirim olarak dönülür.");

    /// <summary>
    /// customerId artık LLM'e sorulan bir parametre değil (bkz. <see cref="CurrentCustomerId"/>) —
    /// yalnızca login'li müşterinin siparişleri sorgulanabilir/listelenebilir. HITL gerekmez
    /// (salt-okunur), bu yüzden <see cref="ExecuteWithApprovalGateAsync"/> KULLANILMAZ.
    /// </summary>
    public AIFunction BuildOrderStatusTool() =>
        AIFunctionFactory.Create(
            ([System.ComponentModel.Description("Sorgulanacak sipariş numarası (zorunlu, ör. '1030')")] string orderId) =>
                _tools.OrderStatusTool(orderId, CurrentCustomerId),
            name: WellKnown.ToolNames.OrderStatus,
            description:
                "Sipariş durumunu sipariş numarasıyla sorgular. Sadece login'li müşterinin kendi siparişleri " +
                "sorgulanabilir; müşteri kimliği login'den otomatik alınır.");

    public AIFunction BuildGetLastOrderTool() =>
        AIFunctionFactory.Create(
            () => _tools.GetLastOrderTool(CurrentCustomerId),
            name: WellKnown.ToolNames.GetLastOrder,
            description:
                "Login'li müşterinin en son siparişini getirir. Parametre gerekmez — müşteri kimliği " +
                "login'den otomatik alınır.");

    public AIFunction BuildGetAllOrdersTool() =>
        AIFunctionFactory.Create(
            () => _tools.GetAllOrdersTool(CurrentCustomerId),
            name: WellKnown.ToolNames.GetAllOrders,
            description:
                "Login'li müşterinin tüm siparişlerini listeler. Parametre gerekmez — müşteri kimliği " +
                "login'den otomatik alınır.");

    /// <summary>
    /// Şikayet durumu sorgulama (SALT-OKUNUR). Müşteri kimliği login'den/çağrı kimliğinden
    /// gelir — LLM'e parametre olarak gösterilmez, dolayısıyla başkasının şikayeti istenemez.
    /// </summary>
    public AIFunction BuildComplaintStatusTool() =>
        AIFunctionFactory.Create(
            ([System.ComponentModel.Description("Sorgulanacak şikayet numarası (örn: 1001)")] string complaintId) =>
                _tools.ComplaintStatusTool(complaintId, CurrentCustomerId),
            name: WellKnown.ToolNames.ComplaintStatus,
            description:
                "Şikayet durumunu şikayet numarasıyla sorgular. Müşteri kimliği otomatik alınır; " +
                "yalnızca kendi şikayetleriniz görünür.");

    /// <summary>Müşterinin tüm şikayetlerini listeler (SALT-OKUNUR).</summary>
    public AIFunction BuildGetAllComplaintsTool() =>
        AIFunctionFactory.Create(
            () => _tools.GetAllComplaintsTool(CurrentCustomerId),
            name: WellKnown.ToolNames.GetAllComplaints,
            description:
                "Login'li müşterinin tüm şikayetlerini listeler. Parametre gerekmez — müşteri kimliği " +
                "otomatik alınır.");

    /// <summary>
    /// Onay gerekmiyorsa tool'u doğrudan çalıştırır; gerekiyorsa kaydı oluşturup KARARI
    /// BEKLEMEDEN "onaya gönderildi" sonucunu döner. Kural <see cref="SideEffectApprovalGate"/>'te
    /// (yazılı ve sesli kanalın ortak kaynağı); bağlam burada ambient scope'tan okunur.
    /// </summary>
    private Task<ToolResult> ExecuteWithApprovalGateAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        Func<ToolResult> executeDirectly,
        Func<ToolResult?>? preflight = null) =>
        _gate.ExecuteAsync(toolName, parameters, executeDirectly, _contextAccessor.Context, preflight);

    /// <summary>
    /// Hangi ajanın hangi tool'u sahiplendiğini çözer — bkz. <see cref="SideEffectApprovalGate.ResolveAgentName"/>.
    /// </summary>
    public static string ResolveAgentName(string toolName) => SideEffectApprovalGate.ResolveAgentName(toolName);

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

        // Dedup artık CreateAsync içinde atomik yapılır — bkz. ExecuteWithApprovalGateAsync'teki
        // aynı gerekçe.
        var req = new ApprovalRequest
        {
            SessionId = ctx?.SessionId,
            // ExecuteWithApprovalGateAsync ile aynı alan — kayıt müşterisiz kalırsa müşterinin
            // geçmiş/okunmamış bildirim uçları (CustomerId'ye göre filtreler) onu göstermez.
            CustomerId = ctx?.CustomerId,
            TraceId = ctx?.TraceId,
            UserQuery = ctx?.UserQuery,
            ToolName = toolName,
            AgentName = agentName,
            Parameters = paramsDict,
            ParamSignature = SideEffectApprovalGate.BuildParamSignature(paramsDict),
            // Çağıran taraf (WorkflowRunner) o an trace'te bulunan gerçek gerekçeyi
            // (PlanningAgent rationale'ı) geçer; yoksa jenerik şablona düşülür.
            Justification = string.IsNullOrWhiteSpace(justification)
                ? string.Format(WellKnown.ApprovalReasons.AgentWantsToCall, agentName)
                : justification
        };
        req = await _approvalQueue.CreateAsync(req, ct);

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

    public async Task ProcessPendingEscalationsAsync(
        ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default)
    {
        await _escalationPolicy.ProcessPendingEscalationsAsync(trace, userQuery, finalResponse, ct);
    }
}

public sealed record ApprovalDecisionResult(bool Approved, string? Reason);
