namespace CustomerSupportBot.Web.Helpers;

/// <summary>
/// Onay kuyruğu için saf görüntü yardımcıları. Kayıttaki müşteri metni, talebi tetikleyen turun cümlesidir: müşteri bir teyit sorusuna
/// cevap verdiyse bu "Evet, gönderebilirsiniz" olur ve talebin ne olduğunu söylemez. Bu yüzden satır özeti talebin kendisinden üretilir.
/// </summary>
public static class ApprovalView
{
    private const int MaxOrderLinesInSummary = 2;
    private const int MaxWordsInConfirmation = 6;

    // Kapalı küme: eşleşmeyen cümle eski haliyle gösterilir, yani hata zararsızdır. Olumsuz biçimler ("gönderme", "onaylamıyorum") bu yüzden
    // kök eşleştirmesiyle değil, tam kelimeyle ayıklanır.
    private static readonly HashSet<string> ConfirmationWords = new(StringComparer.Ordinal)
    {
        "evet", "tamam", "tamamdır", "olur", "peki", "tabii", "tabi", "aynen", "doğru", "kesinlikle", "ok", "okey", "lütfen", "öyle", "kabul",
        "ediyorum", "et", "edin", "edebilirsiniz", "devam", "iptal", "iade", "onayla", "onaylayın", "onaylıyorum", "onayladım", "onaylarım",
        "gönder", "gönderin", "gönderebilirsin", "gönderebilirsiniz", "gönderelim",
        "oluştur", "oluşturun", "oluşturabilirsin", "oluşturabilirsiniz", "oluşturalım",
        "yap", "yapın", "yapabilirsin", "yapabilirsiniz", "yapalım", "ilet", "iletin", "iletebilirsiniz"
    };

    public static bool IsBareConfirmation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var words = Lower(text).Split(
            [' ', '\t', '\n', ',', '.', '!', ';', ':', '-'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length is > 0 and <= MaxWordsInConfirmation && words.All(ConfirmationWords.Contains);
    }

    public static string QuoteLabel(string? userQuery) =>
        IsBareConfirmation(userQuery) ? "Müşterinin onayı" : "Müşterinin son mesajı";

    /// <summary>Kuyruk satırının tek satırlık özeti: talep; talep okunamıyorsa müşterinin cümlesi. Salt onay cümlesi özet sayılmaz.</summary>
    public static string? Summary(
        IReadOnlyList<(string Product, int Quantity)>? orderLines, string? orderId, string? detail, string? userQuery)
    {
        if (orderLines is { Count: > 0 }) return OrderSummary(orderLines);

        if (!string.IsNullOrWhiteSpace(orderId))
            return string.IsNullOrWhiteSpace(detail) ? $"#{orderId}" : $"#{orderId} · {detail.Trim()}";

        return IsBareConfirmation(userQuery) || string.IsNullOrWhiteSpace(userQuery) ? null : userQuery.Trim();
    }

    private static string OrderSummary(IReadOnlyList<(string Product, int Quantity)> lines)
    {
        var shown = string.Join(", ", lines.Take(MaxOrderLinesInSummary).Select(l => $"{l.Quantity} × {l.Product}"));
        var rest = lines.Count - MaxOrderLinesInSummary;
        return rest > 0 ? $"{shown} +{rest} ürün" : shown;
    }

    // Tarayıcıda kültür verisi garanti değil: Türkçe büyük harfler elle çevrilir.
    private static string Lower(string text) => text.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
}
