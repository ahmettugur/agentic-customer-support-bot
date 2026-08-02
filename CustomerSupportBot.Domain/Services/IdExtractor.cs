// Domain/Services/IdExtractor.cs
// Kullanıcı mesajından entity ID'lerini deterministik olarak çıkarır.
//
// ID formatları (prefix yok, minimum 4 hane):
//   order_id     : sipariş bağlamında 4+ haneli sayı (ör. 1030, 1042)
//   complaint_id : şikayet bağlamında 4+ haneli sayı (ör. 1001, 1003)
//   customer_id  : müşteri bağlamında veya bağımsız 4+ haneli sayı (ör. 1008, 1027)

using System.Linq;
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
    // NOT: Türkçe eklemeli bir dil olduğu için ("siparişim", "siparişimin", "siparişi" gibi)
    // sondaki \b kasıtlı olarak yok — ş/i harfi \w kabul edildiğinden ek geldiğinde
    // kelime sınırı oluşmuyor ve eşleşme kaçıyordu.
    private static readonly Regex OrderKeyword = new(
        @"\bsipari[sş]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Şikayet bağlam kelimeleri
    private static readonly Regex ComplaintKeyword = new(
        @"\bşikayet", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Müşteri bağlam kelimeleri
    //
    // "numaram" TEK BAŞINA müşteri sinyalidir ("numaram 1025"), ancak bir entity niteleyicisi
    // tarafından sahiplenilmişse DEĞİLDİR: "sipariş numaram 1041" = "benim SİPARİŞ numaram".
    // Negatif lookbehind'lar bu sahiplenmeyi dışlar (.NET değişken uzunluklu lookbehind destekler).
    //
    // Neden gerekli: mesafe tabanlı seçimde "numaram" sayıya "sipariş"ten daha yakın kalıyordu
    // ("Sipariş numaram 1041" → müşteri boşluğu 1, sipariş boşluğu 9) ve sayı customer_id
    // sanılıyordu. Bu yalnızca prompt hint'ini değil, SessionStateExtractor üzerinden KALICI
    // session state'ini de zehirliyordu: state.CustomerId sipariş numarasıyla doldurulup
    // sonraki tüm turlarda EntityVerifier'a ve CustomerContextProvider'a yanlış müşteri
    // kimliği besliyordu.
    private static readonly Regex CustomerKeyword = new(
        @"\bmü[sş]teri|(?<!\bsipari[sş]\w*\s)(?<!\bşikayet\w*\s)\bnumaram\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            // Tek sayı: en yakın bağlam kelimesi kazanır (whole-text presence değil).
            // Trailing \b kaldırıldığından ör. "müşterim 1008 son siparişi" cümlesinde
            // hem müşteri hem sipariş kelimesi metinde geçer — hangisinin sayıya
            // fiilen daha yakın olduğuna bakmadan "sipariş var" diye karar vermek yanlış olur.
            var singleMatch = numbers[0];
            var singleNum = singleMatch.Value;
            var numStart = singleMatch.Index;
            var numEnd = numStart + singleMatch.Length;

            var orderGap = NearestGap(OrderKeyword, text, numStart, numEnd);
            var complaintGap = NearestGap(ComplaintKeyword, text, numStart, numEnd);
            var customerGap = NearestGap(CustomerKeyword, text, numStart, numEnd);

            if (orderGap is null && complaintGap is null && customerGap is null)
            {
                // Bağlam yok — kısa sorgularda (≤5 token) customer_id varsay
                var tokenCount = text.Split(
                    [' ', '\t', '\n', ',', ';'],
                    StringSplitOptions.RemoveEmptyEntries).Length;
                if (tokenCount <= 5)
                    result.CustomerId = singleNum;
            }
            else
            {
                // Eşitlikte sipariş > şikayet > müşteri (mevcut önceliği korur).
                var winner = new[]
                    {
                        (Kind: 0, Gap: orderGap),
                        (Kind: 1, Gap: complaintGap),
                        (Kind: 2, Gap: customerGap)
                    }
                    .Where(c => c.Gap.HasValue)
                    .OrderBy(c => c.Gap!.Value)
                    .ThenBy(c => c.Kind)
                    .First().Kind;

                if (winner == 0) result.OrderId = singleNum;
                else if (winner == 1) result.ComplaintId = singleNum;
                else result.CustomerId = singleNum;
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

    /// <summary>
    /// Verilen sayıya en yakın regex eşleşmesinin (varsa) karakter boşluğunu döner.
    /// Aradaki boşluk (index farkı değil, gerçek karakter mesafesi) kullanılır —
    /// böylece "müşterim 1008 son siparişi" gibi cümlelerde bitişik kelime kazanır.
    /// </summary>
    private static int? NearestGap(Regex re, string text, int numStart, int numEnd)
    {
        int? best = null;
        foreach (Match m in re.Matches(text))
        {
            int gap;
            if (m.Index + m.Length <= numStart)
                gap = numStart - (m.Index + m.Length);
            else if (m.Index >= numEnd)
                gap = m.Index - numEnd;
            else
                gap = 0;

            if (best is null || gap < best.Value) best = gap;
        }
        return best;
    }

    private static string ExtractWindow(string text, int center, int radius)
    {
        var start = Math.Max(0, center - radius);
        var end   = Math.Min(text.Length, center + radius);
        return text[start..end];
    }
}
