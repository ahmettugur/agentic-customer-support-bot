// Services/Realtime/RealtimeFunctionTools.cs
//
// gpt-realtime-1.5 modeline expose edilen function calling subset'i.
//
// SADECE okuma-only tool'lar burada tanımlanır. Yan-etkili tool'lar
// (OrderPlacement, ComplaintRegistration) HITL gerektirdiği için bilinçli olarak
// **YOK** — model bunları çağıramaz çünkü tanımlarını bile görmez.
//
// Akış:
//   GetToolDefinitions() → session.update'te modele gönderilir
//   DispatchAsync(name, argsJson) → tool çalıştırılır → JSON çıktı döner

using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Services.Realtime;

/// <summary>
/// Realtime native moduna özel okuma-only tool seti ve OpenAI function calling sözleşmesi.
/// </summary>
public sealed class RealtimeFunctionTools
{
    private readonly ILogger<RealtimeFunctionTools> _logger;
    private readonly CustomerSupportToolsService _tools;

    public RealtimeFunctionTools(CustomerSupportToolsService tools, ILogger<RealtimeFunctionTools> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    /// <summary>
    /// OpenAI Realtime <c>session.update</c> içindeki <c>tools</c> dizisi için function tanımları.
    /// Schema'lar JSON Schema (draft 2020-12) formatındadır.
    /// </summary>
    public IReadOnlyList<object> GetToolDefinitions() => ToolDefs;

    /// <summary>UI tarafına bilgi vermek için: hangi tool'lar açıldı?</summary>
    public IReadOnlyList<string> GetToolNames() => ToolNames;

    /// <summary>
    /// Görüşmeyi sonlandırma niyetini işaretleyen özel tool adı. Bridge bu ismi yakaladığında
    /// modelin veda audio'su bittikten sonra WebSocket'i kapatır.
    /// </summary>
    public const string EndConversationToolName = "end_conversation";

    private static readonly string[] ToolNames =
    [
        "product_inquiry_tool",
        "order_status_tool",
        "get_last_order_tool",
        "get_all_orders_tool",
        EndConversationToolName
    ];

    private static readonly object[] ToolDefs =
    [
        new
        {
            type = "function",
            name = "product_inquiry_tool",
            description = "Ürün kataloğundan ürün bilgisi (fiyat, stok) sorgular. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    product_name = new { type = "string", description = "Sorgulanacak ürünün adı veya kısmi adı (örn. 'iPhone', 'Sony WH-1000XM5')" }
                },
                required = new[] { "product_name" }
            }
        },
        new
        {
            type = "function",
            name = "order_status_tool",
            description = "Belirli bir sipariş numarası için durum bilgisini döner. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    order_id = new { type = "string", description = "Sipariş numarası (örn. 'ORD-1', 'ORD-2')" }
                },
                required = new[] { "order_id" }
            }
        },
        new
        {
            type = "function",
            name = "get_last_order_tool",
            description = "Bir müşterinin EN SON oluşturduğu siparişi getirir. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    customer_id = new { type = "string", description = "Müşteri kimlik numarası (örn. 'CUST-1990')" }
                },
                required = new[] { "customer_id" }
            }
        },
        new
        {
            type = "function",
            name = "get_all_orders_tool",
            description = "Bir müşterinin TÜM siparişlerini listeler. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    customer_id = new { type = "string", description = "Müşteri kimlik numarası (örn. 'CUST-1990')" }
                },
                required = new[] { "customer_id" }
            }
        },
        new
        {
            type = "function",
            name = EndConversationToolName,
            description =
                "Kullanıcı görüşmeyi bitirmek istediğini ifade ettiğinde çağır. " +
                "Örnekler: 'görüşürüz', 'teşekkürler kapatabilirsin', 'başka soru yok', 'hoşçakal'. " +
                "ÖNEMLİ: Önce kısa bir veda cümlesi söyle (örn. 'Tabii, iyi günler dilerim'), " +
                "ARDINDAN bu tool'u çağır. Kullanıcı açıkça vedalaşmadıkça çağırma.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    reason = new
                    {
                        type = "string",
                        description = "Kısa neden (örn. 'user_farewell', 'task_completed')."
                    }
                },
                required = Array.Empty<string>()
            }
        }
    ];

    /// <summary>
    /// Tool adına göre dispatch eder. Argümanlar JSON string'inden parse edilir.
    /// Dönen değer her zaman JSON string'idir ve <c>function_call_output</c> olarak
    /// modele geri verilir. Bilinmeyen / blocked tool'lar açıklayıcı bir hata döner.
    /// </summary>
    public Task<string> DispatchAsync(string name, string argumentsJson, CancellationToken ct)
    {
        _logger.LogInformation("RealtimeNative: tool dispatch name={Name} args={Args}",
            name, Truncate(argumentsJson, 200));

        ToolResult result;

        try
        {
            var args = JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject();

            result = name switch
            {
                "product_inquiry_tool"
                    => _tools.ProductInquiryTool(GetString(args, "product_name") ?? ""),

                "order_status_tool"
                    => _tools.OrderStatusTool(GetString(args, "order_id") ?? ""),

                "get_last_order_tool"
                    => _tools.GetLastOrderTool(GetString(args, "customer_id") ?? ""),

                "get_all_orders_tool"
                    => _tools.GetAllOrdersTool(GetString(args, "customer_id") ?? ""),

                EndConversationToolName
                    => ToolResult.Ok("Görüşme sonlandırılıyor.", new
                    {
                        ended = true,
                        reason = GetString(args, "reason") ?? "user_farewell"
                    }),

                // HITL gerektiren tool'lar — model bunları görmemeli ama yine de
                // savunma katmanı: çağrılırsa açıkça reddet.
                "order_placement_tool" or "complaint_registration_tool"
                    => ToolResult.SystemError("FORBIDDEN_IN_VOICE",
                        "Bu işlem güvenlik adımları gerektirir; yazılı sohbet üzerinden yapılmalıdır."),

                _ => ToolResult.SystemError("UNKNOWN_TOOL",
                    $"'{name}' bu modda mevcut değil.")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RealtimeNative: tool argümanları parse edilemedi name={Name}", name);
            result = ToolResult.SystemError("INVALID_ARGS", "Tool argümanları geçersiz JSON.");
        }

        return Task.FromResult(JsonSerializer.Serialize(result, JsonOpts));
    }

    private static string? GetString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        return node.GetValue<string>();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

