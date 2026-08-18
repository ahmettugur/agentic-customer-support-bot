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
                "ve 14 gün içindeki siparişler iade edilebilir; sadece login'li müşterinin kendi siparişleri iade " +
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
    /// Onay gerekmiyorsa tool'u doğrudan çalıştırır. Onay gerekiyorsa <see cref="IApprovalQueue.CreateAsync"/>
    /// ile kaydı oluşturur ve KARARI BEKLEMEDEN hemen "onaya gönderildi" sonucunu döner — turn burada biter.
    /// Gerçek iş (execute), admin karar verdiğinde <see cref="IApprovalExecutionRouter"/> üzerinden ayrıca
    /// tetiklenir (bkz. <c>PostgresApprovalQueue.DecideAsync</c>/<c>InMemoryApprovalQueue.DecideAsync</c>);
    /// sonucu kullanıcıya bir bildirim/badge olarak ulaşır, bu turda değil.
    ///
    /// <para>
    /// <paramref name="preflight"/> — onay kaydı OLUŞTURULMADAN önce çalışan salt-okunur ön kontrol
    /// (ör. sipariş var mı, login'li müşteriye ait mi). Gerçek iş admin kararından sonra çalıştığı
    /// için bu kontrol olmasaydı, baştan başarısız olacağı belli bir talep önce kuyruğa düşer,
    /// admin'in zamanını harcar, onaylanır ve ancak o zaman sessizce başarısız olurdu. Yürütme
    /// anındaki kontrolün YERİNE geçmez (durum arada değişebilir) — birlikte çalışırlar.
    /// </para>
    /// </summary>
    private async Task<ToolResult> ExecuteWithApprovalGateAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        Func<ToolResult> executeDirectly,
        Func<ToolResult?>? preflight = null)
    {
        if (!RequiresApproval(toolName))
            return executeDirectly();

        if (preflight?.Invoke() is { } blocked)
        {
            _logger.LogInformation(
                "[HITL] Onay kaydı oluşturulmadı — ön kontrol reddetti tool={Tool} code={Code}",
                toolName, blocked.Error?.Code);
            return blocked;
        }

        var ctx = _contextAccessor.Context;
        var agentName = ResolveAgentName(toolName);

        // Aynı session'da aynı imzalı bekleyen bir talep varsa (ör. LLM tool çağrısını
        // tekrarladı) yenisini oluşturmak yerine mevcut kaydı döneriz — onaylandığında
        // gerçek iş bir kez tetiklensin diye.
        var paramSig = BuildParamSignature(parameters);
        var existing = ctx?.SessionId is { Length: > 0 } sid
            ? _approvalQueue.GetPending().FirstOrDefault(p =>
                  string.Equals(p.SessionId, sid, StringComparison.Ordinal)
               && string.Equals(p.ToolName, toolName, StringComparison.Ordinal)
               && string.Equals(BuildParamSignature(p.Parameters), paramSig, StringComparison.Ordinal))
            : null;

        var req = existing ?? new ApprovalRequest
        {
            SessionId = ctx?.SessionId,
            CustomerId = ctx?.CustomerId,
            TraceId = ctx?.TraceId,
            UserQuery = ctx?.UserQuery,
            ToolName = toolName,
            AgentName = agentName,
            Parameters = parameters,
            Justification = string.Format(WellKnown.ApprovalReasons.AgentWantsToCall, agentName),
            TimeoutSeconds = _approvalOptions.TimeoutSeconds
        };
        if (existing is null)
            await _approvalQueue.CreateAsync(req);

        return ToolResult.Pending(string.Format(WellKnown.FallbackMessages.ApprovalPending, req.Id));
    }

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

    public async Task ProcessPendingEscalationsAsync(
        ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default)
    {
        await _escalationPolicy.ProcessPendingEscalationsAsync(trace, userQuery, finalResponse, ct);
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
