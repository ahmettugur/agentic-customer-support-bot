// Application/Services/Chat/SuggestedRepliesPolicy.cs
// Tur bittikten sonra müşteriye gösterilecek hızlı yanıt çipleri. Kural tabanlıdır: ek LLM çağrısı, gecikme ve maliyet yok;
// çip metni müşterinin bir sonraki mesajı olarak AYNEN gönderilir, bu yüzden küçük ve kapalı bir kümedir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

public static class SuggestedRepliesPolicy
{
    private const int MaxSuggestions = 3;

    // Karşılaştırma son 140 karakterde soru işareti arar: bot müşteriden bilgi istiyorsa çip yerine cevap beklenir.
    private const int QuestionLookback = 140;

    private const string Cancel = "Siparişimi iptal etmek istiyorum";
    private const string Return = "İade talebi oluşturmak istiyorum";
    private const string OrderComplaint = "Siparişimle ilgili şikayetim var";
    private const string PlaceOrder = "Sipariş vermek istiyorum";
    private const string OrderStatus = "Siparişimin durumunu sorgulamak istiyorum";
    private const string ComplaintStatus = "Şikayetimin durumunu sorgulamak istiyorum";
    private const string Human = "Temsilciyle görüşmek istiyorum";

    private static readonly IReadOnlyDictionary<string, string[]> ByIntent = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [WellKnown.Intents.OrderInquiry] = [Cancel, Return, OrderComplaint],
        [WellKnown.Intents.OrderListing] = [Cancel, Return, OrderComplaint],
        [WellKnown.Intents.ProductInfo] = [PlaceOrder, OrderStatus],
        [WellKnown.Intents.OrderCreation] = [OrderStatus, Human],
        [WellKnown.Intents.OrderCancellation] = [OrderStatus, Human],
        [WellKnown.Intents.ReturnRequest] = [OrderStatus, Human],
        [WellKnown.Intents.Complaint] = [ComplaintStatus, Human],
    };

    /// <summary>
    /// Öneri yoksa boş döner. Yalnızca BAŞARIYLA tamamlanmış (<c>completed</c>), tek niyetli ve müşteriden bilgi
    /// istemeyen turlar öneri alır; hata, eskalasyon, "bulunamadı", bileşik sorgu ve soruyla biten yanıtlarda çip dikkat dağıtır.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string? intent, string? terminationReason, string? response, bool isCompound)
    {
        if (isCompound) return [];
        if (terminationReason != WellKnown.Termination.ReasonCompleted) return [];
        if (string.IsNullOrWhiteSpace(response) || AsksTheCustomer(response)) return [];
        if (intent is null || !ByIntent.TryGetValue(intent, out var suggestions)) return [];
        return suggestions.Take(MaxSuggestions).ToArray();
    }

    /// <summary>Düşük güvende temsilci seçeneğini listenin başına koyar (en fazla <see cref="MaxSuggestions"/>).</summary>
    public static IReadOnlyList<string> WithHuman(IReadOnlyList<string> suggestions) =>
        suggestions.Contains(Human) ? suggestions : [Human, .. suggestions.Take(MaxSuggestions - 1)];

    private static bool AsksTheCustomer(string response)
    {
        var text = response.TrimEnd();
        var tail = text.Length <= QuestionLookback ? text : text[^QuestionLookback..];
        return tail.Contains('?') || tail.Contains('？');
    }
}
