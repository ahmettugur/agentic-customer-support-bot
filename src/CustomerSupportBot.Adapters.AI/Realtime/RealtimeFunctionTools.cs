// Adapters.AI/Realtime/RealtimeFunctionTools.cs
//
// Sesli görüşmede modele expose edilen function calling seti — yazılı sohbetteki iş
// tool'larının tamamı.
//
// Yan etkili tool'lar (sipariş, iptal, iade, şikayet kaydı) yazılı sohbetle AYNI HITL
// onay kapısından geçer (SideEffectApprovalGate): çağrı işlemi yapmaz, onay kaydı oluşturur;
// iş admin onayından sonra yürütülür ve sonuç müşteriye bildirim olarak gider. Eskiden bu
// tool'lar sesli modda hiç tanımlı değildi (onay akışı yalnızca yazılı kanaldaydı).
//
// Tool dispatch iş mantığı Application katmanındadır (RealtimeNativeService).
// Bu sınıf yalnızca OpenAI session.update için tool şemalarını sağlar.

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Sesli görüşmenin tool seti — OpenAI function calling şemaları.
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
    /// Görüşmeyi sonlandırma niyetini işaretleyen özel tool adı. RealtimeNativeService bu ismi yakaladığında
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
        "complaint_status_tool",
        "get_all_complaints_tool",
        "order_placement_tool",
        "order_cancel_tool",
        "return_request_tool",
        "complaint_registration_tool",
        "human_handoff_tool",
        EndConversationToolName
    ];

    private const string ApprovalNote =
        " Bu işlem HEMEN GERÇEKLEŞMEZ: insan onayına gönderilir, sonuç müşteriye bildirim olarak " +
        "iletilir. Çağırmadan önce ayrıntıları müşteriye özetleyip açık onayını al. Müşteri kimliği " +
        "oturumdan otomatik alınır.";

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
            name = "complaint_status_tool",
            description = "Şikayet numarasıyla şikayetin durumunu sorgular. Yalnızca görüşülen müşterinin " +
                          "kendi şikayetleri görünür. Yan etkisi yoktur.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    complaint_id = new { type = "string", description = "Şikayet numarası (örn. '1001')" }
                },
                required = new[] { "complaint_id" }
            }
        },
        new
        {
            type = "function",
            name = "get_all_complaints_tool",
            description = "Görüşülen (login'li) müşterinin TÜM şikayetlerini listeler. Parametre almaz. " +
                          "Yan etkisi yoktur.",
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
            name = "order_placement_tool",
            description = "Yeni sipariş oluşturur. Tek siparişte birden fazla ürün satırı olabilir; müşteri " +
                          "birden fazla ürün istediyse HEPSİNİ tek çağrıda gönder." + ApprovalNote,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    lines = new
                    {
                        type = "array",
                        description = "Sipariş satırları",
                        minItems = 1,
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                product_name = new { type = "string", description = "Sipariş verilecek ürünün adı" },
                                quantity = new { type = "integer", minimum = 1, description = "Adet (en az 1)" }
                            },
                            required = new[] { "product_name", "quantity" }
                        }
                    }
                },
                required = new[] { "lines" }
            }
        },
        new
        {
            type = "function",
            name = "order_cancel_tool",
            description = "Mevcut bir siparişi iptal eder. Yalnızca 'İşleniyor' veya 'Kargolandı' durumundaki " +
                          "siparişler iptal edilebilir." + ApprovalNote,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    order_id = new { type = "string", description = "İptal edilecek sipariş numarası (örn. '1030')" },
                    reason = new { type = "string", description = "İptal sebebi (en az 5 karakter)" }
                },
                required = new[] { "order_id", "reason" }
            }
        },
        new
        {
            type = "function",
            name = "return_request_tool",
            description = "Teslim edilmiş bir sipariş için iade talebi oluşturur. Yalnızca 'Teslim Edildi' " +
                          "durumundaki ve teslimden itibaren 14 gün içindeki siparişler iade edilebilir." + ApprovalNote,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    order_id = new { type = "string", description = "İade edilecek sipariş numarası (örn. '1042')" },
                    reason = new { type = "string", description = "İade sebebi (en az 5 karakter)" }
                },
                required = new[] { "order_id", "reason" }
            }
        },
        new
        {
            type = "function",
            name = "complaint_registration_tool",
            description = "Bir siparişle ilgili şikayet kaydı oluşturur." + ApprovalNote,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    order_id = new { type = "string", description = "Şikayetin ilişkili olduğu sipariş numarası" },
                    complaint_text = new { type = "string", description = "Şikayet açıklaması (en az 10 karakter)" }
                },
                required = new[] { "order_id", "complaint_text" }
            }
        },
        new
        {
            type = "function",
            name = "human_handoff_tool",
            description = "Müşteri açıkça bir insan temsilciyle görüşmek istediğinde çağır. Temsilci kuyruğuna " +
                          "bir talep açılır; müşteriye bir temsilcinin kısa süre içinde bağlanacağını söyle.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    reason = new { type = "string", description = "Müşterinin temsilci isteme sebebi (1-2 cümle)" }
                },
                required = new[] { "reason" }
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
