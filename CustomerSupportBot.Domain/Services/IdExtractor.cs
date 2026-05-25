// Domain/Services/IdExtractor.cs
// Kullanıcı mesajından entity ID'lerini deterministik olarak çıkarır.
//
// ID formatları (prefix yok, minimum 4 hane):
//   order_id     : sipariş bağlamında 4+ haneli sayı (ör. 1030, 1042)
//   complaint_id : şikayet bağlamında 4+ haneli sayı (ör. 1001, 1003)
//   customer_id  : müşteri bağlamında veya bağımsız 4+ haneli sayı (ör. 1008, 1027)

using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

/// <summary>
/// Kullanıcı mesajından entity ID'lerini regex + Türkçe bağlam kelimesiyle çıkarır.
/// Deterministik — aynı input için daima aynı output. LLM çağrısı yok.
/// </summary>
public static class IdExtractor
{
    // 4+ haneli sayı
    private static readonly Regex FourPlusDigitsPattern = new(
        @"\b(\d{4,})\b", RegexOptions.Compiled);

    // Sipariş bağlam kelimeleri
    private static readonly Regex OrderKeyword = new(
        @"\bsipari[sş]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Şikayet bağlam kelimeleri
    private static readonly Regex ComplaintKeyword = new(
        @"\bşikayet\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Müşteri bağlam kelimeleri
    private static readonly Regex CustomerKeyword = new(
        @"\bmü[sş]teri\b|\bnumaram\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Metinden tüm ID türlerini çıkarır. Her alan tek bir değer döner
    /// (birden fazla bulunursa ilk eşleşen).
    /// Bağlam: sipariş kelimesine yakın sayı → order_id, şikayet → complaint_id,
    /// müşteri → customer_id. Bağlam yoksa kısa sorguda tek sayı → customer_id.
    /// </summary>
    public static ExtractedIds Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ExtractedIds();

        var result = new ExtractedIds();
        var numbers = FourPlusDigitsPattern.Matches(text);
        if (numbers.Count == 0) return result;

        var hasOrder    = OrderKeyword.IsMatch(text);
        var hasComplaint = ComplaintKeyword.IsMatch(text);
        var hasCustomer = CustomerKeyword.IsMatch(text);

        // Birden fazla sayı varsa: her biri için yakın bağlam kelimesini bul
        if (numbers.Count >= 2 && (hasOrder || hasComplaint || hasCustomer))
        {
            foreach (Match m in numbers)
            {
                var num = m.Value;
                var pos = m.Index;

                // Sayıya en yakın bağlam kelimesini ara (±60 karakter)
                var window = ExtractWindow(text, pos, 60);
                var isOrder    = OrderKeyword.IsMatch(window);
                var isComplaint = ComplaintKeyword.IsMatch(window);
                var isCustomer = CustomerKeyword.IsMatch(window);

                if (isOrder && result.OrderId == null)
                    result.OrderId = num;
                else if (isComplaint && result.ComplaintId == null)
                    result.ComplaintId = num;
                else if (isCustomer && result.CustomerId == null)
                    result.CustomerId = num;
            }

            // Kalan sayıları ata (bağlamsız fallback)
            if (result.OrderId == null && hasOrder)
                result.OrderId = numbers[0].Value;
            if (result.ComplaintId == null && hasComplaint)
                result.ComplaintId = numbers[0].Value;
            if (result.CustomerId == null)
                result.CustomerId = numbers[^1].Value; // son sayıyı fallback customer'a ver
        }
        else
        {
            // Tek sayı veya bağlam yok
            var singleNum = numbers[0].Value;

            if (hasOrder)
                result.OrderId = singleNum;
            else if (hasComplaint)
                result.ComplaintId = singleNum;
            else if (hasCustomer)
                result.CustomerId = singleNum;
            else
            {
                // Bağlam yok — kısa sorgularda (≤5 token) customer_id varsay
                var tokenCount = text.Split(
                    [' ', '\t', '\n', ',', ';'],
                    StringSplitOptions.RemoveEmptyEntries).Length;
                if (tokenCount <= 5)
                    result.CustomerId = singleNum;
            }
        }

        return result;
    }

    /// <summary>
    /// ExtractedIds'i PlanningAgent/Specialist prompt'una enjekte edilecek
    /// bir system mesajı formatına çevirir. Hiçbir ID bulunamadıysa boş döner.
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
        lines.Add("Not: Ekstraksiyon yanlış görünüyorsa kullanıcıya doğrulat.");

        return string.Join('\n', lines);
    }

    private static string ExtractWindow(string text, int center, int radius)
    {
        var start = Math.Max(0, center - radius);
        var end   = Math.Min(text.Length, center + radius);
        return text[start..end];
    }
}
