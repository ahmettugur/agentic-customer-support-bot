// Application/Services/Providers/ProductRecommendationContextProvider.cs
// IContextProvider — IRecommendationService'in ürettiği öneriyi (varsa) bağlama ekler.
//
// Bu blok bir TALİMAT değildir. ProductAgent'a "uygunsa bahsedebilirsin, zorlama" diyen bir
// öneridir; hiçbir tool'u tetiklemez, otonom bir eylem başlatmaz. Şikayet/sipariş
// akışlarındaki ajanlar bu bloğu görmezden gelmekte serbesttir — susma kararının çoğu zaten
// IRecommendationService'te verildiği için (duygu/veri yeterliliği), bu blok yalnızca
// GERÇEKTEN uygun olduğunda üretilir.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class ProductRecommendationContextProvider : IContextProvider
{
    private readonly IRecommendationService _recommendations;
    private readonly ILogger<ProductRecommendationContextProvider> _logger;

    public string Name => "ProductRecommendation";

    /// <summary>
    /// SemanticMemory (7)'den sonra, CustomerContext (10)'dan önce — öneri, sipariş/şikayet
    /// gibi kritik olgulardan daha düşük öncelikli; bütçe dolarsa önce bu düşer.
    /// </summary>
    public int Order => 8;

    public ProductRecommendationContextProvider(
        IRecommendationService recommendations,
        ILogger<ProductRecommendationContextProvider> logger)
    {
        _recommendations = recommendations;
        _logger = logger;
    }

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var recs = _recommendations.Recommend(session);
        if (recs.Count == 0) return Task.FromResult<string?>(null);

        var lines = recs.Select(r => $"- {r.ProductName} — {r.Reason}");
        var text =
            "## 💡 Olası İlgi Alanı (isteğe bağlı, YALNIZCA uygun bağlamda bahset — zorlama)\n" +
            string.Join("\n", lines);

        _logger.LogDebug("ProductRecommendation context: customerId={Id} count={Count}",
            session.State.AuthenticatedCustomerId, recs.Count);
        return Task.FromResult<string?>(text);
    }
}
