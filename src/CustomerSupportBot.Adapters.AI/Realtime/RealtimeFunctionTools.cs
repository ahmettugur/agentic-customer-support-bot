// Adapters.AI/Realtime/RealtimeFunctionTools.cs
//
// gpt-realtime-1.5 modeline expose edilen function calling subset'i.
//
// SADECE okuma-only tool'lar burada tanımlanır. Yan-etkili tool'lar
// (OrderPlacement, ComplaintRegistration) HITL gerektirdiği için bilinçli olarak
// **YOK** — model bunları çağıramaz çünkü tanımlarını bile görmez.
//
// Tool dispatch iş mantığı Application katmanına taşındı (RealtimeNativeService).
// Bu sınıf yalnızca OpenAI session.update için tool şemalarını sağlar.

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Realtime native moduna özel okuma-only tool seti — OpenAI function calling şemaları.
/// </summary>
public sealed class RealtimeFunctionTools
{
    public RealtimeFunctionTools() { }

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
        "product_list_tool",
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
            name = "product_list_tool",
            description = "Ürün kataloğunu listeler. Kategori belirtilirse sadece o kategorideki ürünleri, belirtilmezse tüm ürünleri döner. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    category = new { type = "string", description = "Filtrelenecek kategori adı (opsiyonel, örn. 'Elektronik', 'İçecek'). Boş bırakılırsa tüm katalog döner." }
                },
                required = Array.Empty<string>()
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
                    order_id = new { type = "string", description = "Sipariş numarası (örn. '1030', '1042')" }
                },
                required = new[] { "order_id" }
            }
        },
        new
        {
            type = "function",
            name = "get_last_order_tool",
            description = "Görüşülen (login'li) müşterinin EN SON siparişini getirir. Parametre almaz — " +
                          "müşteri kimliği oturumdan otomatik alınır. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        },
        new
        {
            type = "function",
            name = "get_all_orders_tool",
            description = "Görüşülen (login'li) müşterinin TÜM siparişlerini listeler. Parametre almaz — " +
                          "müşteri kimliği oturumdan otomatik alınır. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
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

}
