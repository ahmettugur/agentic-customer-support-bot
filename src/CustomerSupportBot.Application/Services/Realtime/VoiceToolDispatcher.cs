// Application/Services/Realtime/VoiceToolDispatcher.cs
// Sesli kanalın tool çalıştırma mantığı — iki ses motoru da aynı iş kurallarını ve onay kapısını kullanır.

using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>
/// Model hangi motordan gelirse gelsin tool çağrısının sonucu aynıdır: okuma tool'ları hemen çalışır, yan etkili olanlar
/// <see cref="SideEffectApprovalGate"/> ile insan onayına gider, müşteri kimliği yalnızca doğrulanmış oturumdan okunur.
/// </summary>
internal sealed class VoiceToolDispatcher(
    CustomerSupportToolsService tools,
    ILogger logger,
    IEscalationSink? escalations = null,
    SideEffectApprovalGate? approvalGate = null)
{
    /// <summary>Görüşmeyi sonlandırma tool'unun adı; adapter'daki tool tanımıyla aynı olmalı.</summary>
    internal const string EndConversationToolName = "end_conversation";

    private readonly CustomerSupportToolsService _tools = tools;
    private readonly ILogger _logger = logger;
    private readonly IEscalationSink? _escalations = escalations;
    private readonly SideEffectApprovalGate? _approvalGate = approvalGate;

    /// <summary>Beklenmeyen dispatch hatasının modele dönecek JSON'u.</summary>
    public static string ErrorJson(Exception ex) => JsonSerializer.Serialize(new
    {
        success = false,
        error = new { code = "TOOL_DISPATCH_ERROR", message = ex.Message }
    }, ToolResultJsonOpts);

    /// <summary>
    /// Tool adına göre işi yönlendirir. Sesli kanal yazılı sohbetin iş tool'larının TAMAMINI
    /// çalıştırır; yan etkili olanlar yazılı sohbetle AYNI onay kapısından geçer
    /// (<see cref="RunSideEffectToolAsync"/>).
    /// </summary>
    public async Task<string> DispatchAsync(
        string name, string argumentsJson, AgentSession session, string? userQuery)
    {
        // customer_id LLM argümanından ASLA okunmaz — sadece login'li kullanıcının
        // JWT-doğrulanmış kimliği kullanılır; aksi halde model başka bir müşterinin
        // siparişlerini isteyebilir ya da onun adına işlem başlatabilirdi.
        var customerId = session.State.AuthenticatedCustomerId ?? "";
        ToolResult result;
        try
        {
            var args = JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject();

            result = name switch
            {
                WellKnown.ToolNames.ProductInquiry   => _tools.ProductInquiryTool(GetString(args, "product_name") ?? ""),
                WellKnown.ToolNames.ProductList      => _tools.ProductListTool(GetString(args, "category")),
                WellKnown.ToolNames.OrderStatus      => _tools.OrderStatusTool(GetString(args, "order_id") ?? "", customerId),
                WellKnown.ToolNames.GetLastOrder     => _tools.GetLastOrderTool(customerId),
                WellKnown.ToolNames.GetAllOrders     => _tools.GetAllOrdersTool(customerId),
                WellKnown.ToolNames.ComplaintStatus  => _tools.ComplaintStatusTool(GetString(args, "complaint_id") ?? "", customerId),
                WellKnown.ToolNames.GetAllComplaints => _tools.GetAllComplaintsTool(customerId),

                WellKnown.ToolNames.OrderPlacement
                    or WellKnown.ToolNames.OrderCancel
                    or WellKnown.ToolNames.ReturnRequest
                    or WellKnown.ToolNames.ComplaintRegistration =>
                    await RunSideEffectToolAsync(name, args, session.SessionId, customerId, userQuery),

                WellKnown.ToolNames.HumanHandoff =>
                    await RequestHumanHandoffAsync(session.SessionId, GetString(args, "reason"), userQuery),

                EndConversationToolName =>
                    ToolResult.Ok("Görüşme sonlandırılıyor.", new
                    {
                        ended  = true,
                        reason = GetString(args, "reason") ?? "user_farewell"
                    }),
                _ => ToolResult.SystemError("UNKNOWN_TOOL", $"'{name}' bu modda mevcut değil.")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Voice: tool argümanları parse edilemedi name={Name}", name);
            result = ToolResult.SystemError("INVALID_ARGS", "Tool argümanları geçersiz JSON.");
        }

        return JsonSerializer.Serialize(result, ToolResultJsonOpts);
    }

    /// <summary>
    /// Yan etkili tool (sipariş, iptal, iade, şikayet kaydı). İşlem burada YAPILMAZ: yazılı
    /// sohbetle aynı <see cref="SideEffectApprovalGate"/> onay kaydı oluşturur ve "onaya gönderildi"
    /// döner; iş admin onayından sonra <c>IApprovalExecutionRouter</c> ile yürütülür, sonuç
    /// müşteriye bildirim olarak gider. Parametre anahtarları yazılı kanalla aynıdır — yürütücü
    /// onları okur ve aynı talebin iki kanaldan gelen kopyaları tek onay kaydında birleşir.
    /// </summary>
    private async Task<ToolResult> RunSideEffectToolAsync(
        string name, JsonObject args, string sessionId, string customerId, string? userQuery)
    {
        if (_approvalGate is null)
        {
            return ToolResult.SystemError("APPROVAL_UNAVAILABLE",
                "Bu işlem şu anda sesli kanaldan başlatılamıyor; lütfen yazılı sohbeti kullanın.");
        }

        var context = new ApprovalContext(
            SessionId: sessionId,
            TraceId: null,
            UserQuery: userQuery ?? VoiceTurnPairer.Placeholder,
            CustomerId: customerId);

        switch (name)
        {
            case WellKnown.ToolNames.OrderPlacement:
            {
                var lines = ParseOrderLines(args);
                return await _approvalGate.ExecuteAsync(
                    name,
                    new Dictionary<string, object?> { ["lines"] = lines, ["customerId"] = customerId },
                    () => _tools.OrderPlacementTool(lines, customerId),
                    context,
                    preflight: () => ValidateOrderLines(lines));
            }

            case WellKnown.ToolNames.ComplaintRegistration:
            {
                var orderId = GetString(args, "order_id") ?? "";
                var complaintText = GetString(args, "complaint_text") ?? "";
                return await _approvalGate.ExecuteAsync(
                    name,
                    new Dictionary<string, object?>
                    {
                        ["orderId"] = orderId, ["complaintText"] = complaintText, ["customerId"] = customerId
                    },
                    () => _tools.ComplaintRegistrationTool(orderId, complaintText, customerId),
                    context,
                    preflight: () => _tools.ValidateOrderActionable(orderId, customerId));
            }

            default: // OrderCancel / ReturnRequest
            {
                var orderId = GetString(args, "order_id") ?? "";
                var reason = GetString(args, "reason") ?? "";
                return await _approvalGate.ExecuteAsync(
                    name,
                    new Dictionary<string, object?>
                    {
                        ["orderId"] = orderId, ["reason"] = reason, ["customerId"] = customerId
                    },
                    name == WellKnown.ToolNames.OrderCancel
                        ? () => _tools.OrderCancelTool(orderId, reason, customerId)
                        : () => _tools.ReturnRequestTool(orderId, reason, customerId),
                    context,
                    preflight: () => _tools.ValidateOrderActionable(orderId, customerId));
            }
        }
    }

    /// <summary>
    /// Müşteri insan temsilci istedi: temsilci kuyruğuna (eskalasyon) bir talep açılır —
    /// yazılı sohbette <c>human_handoff_tool</c> çağrısının eskalasyona çevrilmesiyle aynı sonuç.
    /// Oturuma bağlı sohbet sayfası bunu "temsilci bekleniyor" olarak görür (handoff_pending).
    /// </summary>
    private async Task<ToolResult> RequestHumanHandoffAsync(string sessionId, string? reason, string? userQuery)
    {
        var result = CustomerSupportToolsService.HumanHandoffTool(reason ?? "");
        if (!result.Success) return result;

        if (_escalations is null)
        {
            return ToolResult.SystemError("HANDOFF_UNAVAILABLE",
                "Temsilci talebi şu anda alınamıyor; lütfen yazılı sohbetten tekrar deneyin.");
        }

        await _escalations.CreateAsync(new EscalationRequest
        {
            SessionId = sessionId,
            AgentName = WellKnown.AgentNames.HumanHandoff,
            UserQuery = userQuery ?? VoiceTurnPairer.Placeholder,
            Reason = reason!.Trim()
        });
        return result;
    }

    private static OrderLineRequest[] ParseOrderLines(JsonObject args)
    {
        if (!args.TryGetPropertyValue("lines", out var node) || node is not JsonArray array) return [];

        return array
            .OfType<JsonObject>()
            .Select(line => new OrderLineRequest(
                GetString(line, "product_name") ?? "",
                int.TryParse(GetString(line, "quantity"), out var quantity) ? quantity : 0))
            .ToArray();
    }

    /// <summary>
    /// Onay kaydı oluşturulmadan önce sipariş satırlarının biçim kontrolü — eksik/bozuk bir
    /// talep (ses tanıma hatası, eksik adet) admin kuyruğuna düşmesin.
    /// </summary>
    private static ToolResult? ValidateOrderLines(IReadOnlyList<OrderLineRequest> lines)
    {
        if (lines.Count == 0)
            return ToolResult.ValidationError("Sipariş için en az bir ürün ve adet gerekli.", "lines");
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.ProductName)))
            return ToolResult.ValidationError("Her sipariş satırında ürün adı olmalı.", "product_name");
        if (lines.Any(l => l.Quantity <= 0))
            return ToolResult.ValidationError("Her ürün için en az 1 adet belirtilmeli.", "quantity");
        return null;
    }

    /// <summary>
    /// Argümanı metin olarak okur. Model sayısal alanları (sipariş numarası, adet) bazen
    /// string yerine sayı olarak gönderir; eskiden bu durumda okuma istisna fırlatıp tool
    /// çağrısını "TOOL_DISPATCH_ERROR" ile düşürüyordu.
    /// </summary>
    private static string? GetString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString();
    }

    private static readonly JsonSerializerOptions ToolResultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
