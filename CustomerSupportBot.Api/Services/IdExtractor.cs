// Services/IdExtractor.cs
// Kullanıcı mesajından entity ID'leri deterministik olarak çıkarır.
// LLM'in "hangisi customer_id, hangisi order_id?" gibi sorular sorarak
// Gereksiz turlar yaratmasını önler.
//
// ID formatları (FakeDatabase'den):
//   Order_id     : "ORD-N"   (ör. ORD-1, ORD-2)
//   Complaint_id : "CMP-N"   (ör. CMP-1, CMP-2)
//   Customer_id  : "CUST-N" (ör. "CUST-1990")
//   Product_id   : serbest metin (ör. "Dell XPS 15")

using System.Text.RegularExpressions;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Kullanıcı mesajından entity ID'lerini regex ile çıkarır.
/// Deterministik — aynı input için daima aynı output. LLM çağrısı yok.
/// </summary>
public static class IdExtractor
{
    // Sipariş: ORD-1, ORD_1, ord-001, ORD 1 hepsi tanınır
    private static readonly Regex OrderIdPattern = new(
        @"\bORD[-_\s]?(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Şikayet: CMP-1, CMP_1, cmp-001
    private static readonly Regex ComplaintIdPattern = new(
        @"\bCMP[-_\s]?(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Müşteri (prefixli): CUST-001, CUST_1, CUST 001
    private static readonly Regex CustomerIdPrefixPattern = new(
        @"\bCUST[-_\s]?(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Müşteri (saf rakam): 3-5 haneli sayı. Yıl benzeri.
    // NOT: ORD-1'deki "1"i yakalamaması için BOUNDARY ve kelime sınırları dikkatli.
    private static readonly Regex NumericOnlyPattern = new(
        @"(?<![\w-])(\d{3,5})(?![\w-])",
        RegexOptions.Compiled);

    /// <summary>
    /// Metinden tüm ID türlerini çıkarır. Her alan tek bir değer döner
    /// (birden fazla bulunursa ilk eşleşen).
    /// </summary>
    public static ExtractedIds Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ExtractedIds();

        var result = new ExtractedIds();

        // 1) order_id — en spesifik pattern önce
        var orderMatch = OrderIdPattern.Match(text);
        if (orderMatch.Success)
        {
            result.OrderId = $"ORD-{orderMatch.Groups[1].Value.TrimStart('0')}";
            if (result.OrderId == "ORD-") result.OrderId = $"ORD-{orderMatch.Groups[1].Value}";
        }

        // 2) complaint_id
        var complaintMatch = ComplaintIdPattern.Match(text);
        if (complaintMatch.Success)
        {
            result.ComplaintId = $"CMP-{complaintMatch.Groups[1].Value.TrimStart('0')}";
            if (result.ComplaintId == "CMP-") result.ComplaintId = $"CMP-{complaintMatch.Groups[1].Value}";
        }

        // 3) customer_id (prefixli)
        var custPrefixMatch = CustomerIdPrefixPattern.Match(text);
        if (custPrefixMatch.Success)
        {
            result.CustomerId = $"CUST-{custPrefixMatch.Groups[1].Value}";
        }
        else
        {
            // 4) customer_id (saf rakam fallback) — yalnızca bağlam yeterince
            // Güvenliyse çıkar. "2025 yılında siparişim geldi" gibi cümlelerde
            // "2025"i müşteri ID'si sanmasın diye şu koşulları ararız:
            //   A) Query'de BAŞKA bir ID var (ORD-*, CMP-*, CUST-*) → bağlam açık
            //   B) Query ≤ 4 token — kısa ve ID-odaklı görünüyor
            var hasAnchorId = orderMatch.Success
                              || complaintMatch.Success
                              || custPrefixMatch.Success;
            var tokenCount = text.Split(
                new[] { ' ', '\t', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries).Length;
            var isShortQuery = tokenCount <= 4;

            if (hasAnchorId || isShortQuery)
            {
                // ORD-/CMP-/CUST- içindeki sayıları ELE —
                // Bu amaçla textten o eşleşmeleri maskele, sonra numeric ara.
                var maskedText = OrderIdPattern.Replace(text, "");
                maskedText = ComplaintIdPattern.Replace(maskedText, "");
                maskedText = CustomerIdPrefixPattern.Replace(maskedText, "");

                var numericMatch = NumericOnlyPattern.Match(maskedText);
                if (numericMatch.Success)
                {
                    result.CustomerId = numericMatch.Groups[1].Value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// ExtractedIds'i PlanningAgent/Specialist prompt'una enjekte edilecek
    /// Bir system mesajı formatına çevirir. Hiçbir ID bulunamadıysa boş döner.
    ///
    /// Ek olarak sipariş sorgulaması için öncelik kuralı da belirtir:
    ///   - order_id varsa → order_status_tool (öncelikli)
    ///   - sadece customer_id varsa → get_last_order_tool (fallback)
    /// Bu sayede LLM gereksiz yere ikinci bir kimlik istemez.
    /// </summary>
    public static string? BuildHintMessage(ExtractedIds ids)
    {
        if (!ids.HasAny) return null;

        var lines = new List<string>
        {
            "[ENTITY EXTRACTION — deterministik regex ile çıkarıldı]",
            "Kullanıcı mesajından aşağıdaki ID'ler otomatik çıkarıldı. " +
            "Bu bilgileri KULLAN, tekrar kullanıcıya sorma:"
        };

        if (!string.IsNullOrEmpty(ids.OrderId))
            lines.Add($"- order_id = \"{ids.OrderId}\"");
        if (!string.IsNullOrEmpty(ids.CustomerId))
            lines.Add($"- customer_id = \"{ids.CustomerId}\"");
        if (!string.IsNullOrEmpty(ids.ComplaintId))
            lines.Add($"- complaint_id = \"{ids.ComplaintId}\"");

        // Sipariş sorgusu için tool öncelik ipucu
        lines.Add("");
        lines.Add("SİPARİŞ SORGUSU ÖNCELİK KURALI:");
        if (!string.IsNullOrEmpty(ids.OrderId))
        {
            lines.Add("  - order_id MEVCUT → 'order_status_tool' kullan (order_id ile sorgula).");
            lines.Add("  - customer_id TEKRAR SORMA; order_id tek başına yeterlidir.");
        }
        else if (!string.IsNullOrEmpty(ids.CustomerId))
        {
            lines.Add("  - order_id YOK, customer_id MEVCUT → 'get_last_order_tool' kullan " +
                      "(müşterinin en son siparişini getir).");
            lines.Add("  - Kullanıcı açıkça 'tüm siparişlerim' / 'sipariş geçmişim' demediyse " +
                      "'get_all_orders_tool' ÇAĞIRMA — son sipariş varsayılandır.");
            lines.Add("  - order_id TEKRAR SORMA; customer_id tek başına yeterlidir.");
        }

        lines.Add("");
        lines.Add("Not: Ekstraksiyon yanlış görünüyorsa (ör. 'CUST-1990' " +
                  "aslında yıl bilgisi) kullanıcıya doğrulat.");

        return string.Join('\n', lines);
    }
}

/// <summary>Metinden çıkarılmış ID'ler.</summary>
public class ExtractedIds
{
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string? ComplaintId { get; set; }

    public bool HasAny =>
        !string.IsNullOrEmpty(OrderId) ||
        !string.IsNullOrEmpty(CustomerId) ||
        !string.IsNullOrEmpty(ComplaintId);
}
