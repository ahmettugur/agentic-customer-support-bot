// Application/Services/Personalization/RecommendationService.cs
// IRecommendationService implementasyonu — kural tabanlı, LLM ÇAĞIRMAZ.
//
// Kural: müşterinin en yeni ürün ilgisiyle AYNI KATEGORİDE, henüz ilgilenmediği, stokta olan
// ürünleri önerir. Kolaylıkla genişleyebilecek bir yer değil — susma kuralları kadar önemli
// olan şey, bu servisin ne YAPMADIĞI: işbirlikçi filtreleme yok, satın alma geçmişi analizi
// yok, LLM'e "bu müşteriye ne satarız" diye sorulmuyor. Veri bunu desteklemiyor (yalnızca
// bu müşterinin kendi ilgi kaydı var, başka müşterilerle karşılaştırma yok) ve destek
// bağlamında agresif bir öneri motoru istenmiyor.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Personalization;

public sealed class RecommendationService : IRecommendationService
{
    // Bu niyetlerde müşteri zaten aktif bir sorunla/işlemle meşgul — araya ürün önerisi
    // sokmak, "şikayetimi çözmek yerine bana bir şey satmaya çalışıyor" izlenimi verir.
    private static readonly HashSet<string> SilentIntents = new(StringComparer.Ordinal)
    {
        WellKnown.Intents.Complaint,
        WellKnown.Intents.ReturnRequest,
        WellKnown.Intents.OrderCancellation,
        WellKnown.Intents.HumanHandoffRequest,
    };

    private readonly ICustomerUnderstandingService _understanding;
    private readonly IProductCatalogRepository _products;

    public RecommendationService(
        ICustomerUnderstandingService understanding,
        IProductCatalogRepository products)
    {
        _understanding = understanding;
        _products = products;
    }

    public IReadOnlyList<ProductRecommendation> Recommend(AgentSession session, int maxResults = 2)
    {
        // Susma kuralı 1: müşteri şikayetçi/öfkeliyse hiç öneri üretilmez. Bu, agresif
        // satış izleniminin somut karşılığı — destek isteyen birine ürün satmaya çalışmak.
        if (session.State.Sentiment is WellKnown.Sentiments.Negative or WellKnown.Sentiments.Angry)
            return [];

        // Susma kuralı 1b: bu turun niyeti zaten bir sorun/işlem çözme niyetiyse (iade,
        // iptal, şikayet, temsilci talebi) susulur. Bilerek CustomerUnderstanding'den değil
        // session.State'ten okunuyor — CustomerUnderstanding kasıtlı olarak yalnızca uzun
        // vadeli sentezi taşır, "şu an" bilgisini taşımaz (bkz. CustomerUnderstandingService
        // XML doc, "Bilerek taşımadığı" bölümü). Sentiment kontrolüyle aynı sebepten
        // Build() çağrılmadan önce, ucuz bir kısa devre olarak yapılır.
        if (session.State.CurrentIntent is not null && SilentIntents.Contains(session.State.CurrentIntent))
            return [];

        var understanding = _understanding.Build(session);
        // Susma kuralı 2: hiç ilgi kaydı yoksa (yeni müşteri, hiç ürün sorulmamış) önerecek
        // bir şey yok — rastgele bir ürün önermek "understanding"e dayanmaz, tahmindir.
        if (understanding is null || understanding.ProductInterests.Count == 0)
            return [];

        var topInterest = understanding.ProductInterests[0];
        var interestProduct = _products.FindProduct(topInterest);
        // Susma kuralı 3: ilgilenilen ürün adı kataloğa artık çözülemiyorsa (silindi/değişti)
        // veya kategorisiz bırakılmışsa öneri kurulamaz.
        if (interestProduct is null || string.IsNullOrWhiteSpace(interestProduct.Category))
            return [];

        var alreadyKnown = new HashSet<string>(understanding.ProductInterests, StringComparer.OrdinalIgnoreCase);

        var candidates = _products.GetAll()
            .Where(p => string.Equals(p.Category, interestProduct.Category, StringComparison.Ordinal))
            .Where(p => p.Stock > 0)
            .Where(p => !alreadyKnown.Contains(p.Name))
            .Take(maxResults)
            .Select(p => new ProductRecommendation(
                p.Name,
                $"'{topInterest}' ile aynı kategoride ({p.Category})",
                p.Category))
            .ToList();

        return candidates;
    }
}
