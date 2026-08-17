// Tests/Services/Personalization/RecommendationServiceTests.cs
//
// IRecommendationService: kural tabanlı (LLM'siz) ürün önerisi. Bu testlerin asıl konusu
// SUSMA kuralları — bir destek botunda öneri motoru, ne zaman konuşacağından çok ne zaman
// SUSACAĞIYLA doğru olur. Şikayetçi bir müşteriye, ya da hakkında hiçbir şey bilmediğimiz bir
// müşteriye öneri sunmak agresif satış izlenimi verir.

using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Application.Tests;

public class RecommendationServiceTests
{
    private static AgentSession Session(string sentiment = "neutral", string? currentIntent = null) =>
        new()
        {
            SessionId = "s",
            State = new SessionState { AuthenticatedCustomerId = "1027", Sentiment = sentiment, CurrentIntent = currentIntent }
        };

    private static CustomerUnderstanding Understanding(params string[] productInterests) => new(
        CustomerId: "1027", Persona: null, AdminNote: null, Traits: [],
        PreferredTone: "neutral", PreferredLanguage: "tr",
        ProductInterests: productInterests, TopIntents: [],
        AverageRating: null, RatingCount: 0, TotalSessions: 1, TotalTurns: 1,
        LastConsolidatedAt: null);

    private static (RecommendationService Service, ICustomerUnderstandingService Understanding, IProductCatalogRepository Products) Build()
    {
        var understanding = Substitute.For<ICustomerUnderstandingService>();
        var products = Substitute.For<IProductCatalogRepository>();
        return (new RecommendationService(understanding, products), understanding, products);
    }

    // ═══ Susma kuralları ═══

    [Theory]
    [InlineData(WellKnown.Sentiments.Negative)]
    [InlineData(WellKnown.Sentiments.Angry)]
    public void Recommend_NegativeOrAngrySentiment_ReturnsEmpty(string sentiment)
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler")
        ]);

        var result = svc.Recommend(Session(sentiment));

        result.Should().BeEmpty("şikayetçi/öfkeli müşteriye öneri sunmak agresif satış izlenimi verir");
        understanding.DidNotReceive().Build(Arg.Any<AgentSession>());
    }

    [Theory]
    [InlineData(WellKnown.Intents.Complaint)]
    [InlineData(WellKnown.Intents.ReturnRequest)]
    [InlineData(WellKnown.Intents.OrderCancellation)]
    [InlineData(WellKnown.Intents.HumanHandoffRequest)]
    public void Recommend_ActiveIssueIntent_ReturnsEmpty(string intent)
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler")
        ]);

        var result = svc.Recommend(Session(currentIntent: intent));

        result.Should().BeEmpty("aktif bir sorun/işlem çözülürken araya ürün önerisi sokmak yanlış izlenim verir");
        understanding.DidNotReceive().Build(Arg.Any<AgentSession>());
    }

    [Theory]
    [InlineData(WellKnown.Intents.OrderInquiry)]
    [InlineData(WellKnown.Intents.ProductInfo)]
    [InlineData(WellKnown.Intents.General)]
    [InlineData(null)]
    public void Recommend_NonBlockingIntent_StillRecommends(string? intent)
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler")
        ]);

        var result = svc.Recommend(Session(currentIntent: intent));

        result.Should().ContainSingle();
    }

    [Fact]
    public void Recommend_NoUnderstanding_ReturnsEmpty()
    {
        var (svc, understanding, _) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns((CustomerUnderstanding?)null);

        svc.Recommend(Session()).Should().BeEmpty();
    }

    [Fact]
    public void Recommend_NoProductInterests_ReturnsEmpty()
    {
        var (svc, understanding, _) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding());

        svc.Recommend(Session()).Should().BeEmpty("hakkında hiçbir şey bilmediğimiz müşteriye rastgele öneri, tahmindir — understanding değil");
    }

    [Fact]
    public void Recommend_InterestProductNoLongerInCatalog_ReturnsEmpty()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Kaldırılmış Ürün"));
        products.FindProduct("Kaldırılmış Ürün").Returns((ProductInfo?)null);

        svc.Recommend(Session()).Should().BeEmpty();
    }

    [Fact]
    public void Recommend_OnlyCandidateIsOutOfStock_ReturnsEmpty()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 0, "Bira", "İçecekler")  // stokta yok
        ]);

        svc.Recommend(Session()).Should().BeEmpty();
    }

    [Fact]
    public void Recommend_OnlyCandidateIsCategoryItself_ReturnsEmpty()
    {
        // Kategoride tek ürün var ve o da zaten ilgilenilen ürünün kendisi.
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Zeytinyağı"));
        products.FindProduct("Zeytinyağı").Returns(new ProductInfo(21.35m, 40, "Zeytinyağı", "Yağlar"));
        products.GetAll().Returns([new ProductInfo(21.35m, 40, "Zeytinyağı", "Yağlar")]);

        svc.Recommend(Session()).Should().BeEmpty();
    }

    // ═══ Gerçek öneri üretimi ═══

    [Fact]
    public void Recommend_SameCategoryInStock_ReturnsRecommendation()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler"),
            new ProductInfo(21.35m, 40, "Zeytinyağı", "Yağlar")  // farklı kategori
        ]);

        var result = svc.Recommend(Session());

        result.Should().ContainSingle();
        result[0].ProductName.Should().Be("Bira");
        result[0].Category.Should().Be("İçecekler");
        result[0].Reason.Should().Contain("Çay");
    }

    /// <summary>Zaten ilgilendiği ürünlerin hiçbiri tekrar önerilmemeli — sadece top interest değil, listedeki tümü.</summary>
    [Fact]
    public void Recommend_ExcludesAllKnownInterests_NotJustTopOne()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay", "Bira"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler"),
            new ProductInfo(46m, 100, "Kahve", "İçecekler")
        ]);

        var result = svc.Recommend(Session());

        result.Should().ContainSingle();
        result[0].ProductName.Should().Be("Kahve");
    }

    [Fact]
    public void Recommend_RespectsMaxResults()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(14m, 60, "Bira", "İçecekler"),
            new ProductInfo(46m, 100, "Kahve", "İçecekler")
        ]);

        svc.Recommend(Session(), maxResults: 1).Should().HaveCount(1);
    }

    /// <summary>Sızıntı testi: farklı kategorideki ürün asla önerilmemeli.</summary>
    [Fact]
    public void Recommend_NeverRecommendsDifferentCategory()
    {
        var (svc, understanding, products) = Build();
        understanding.Build(Arg.Any<AgentSession>()).Returns(Understanding("Çay"));
        products.FindProduct("Çay").Returns(new ProductInfo(18m, 40, "Çay", "İçecekler"));
        products.GetAll().Returns([
            new ProductInfo(18m, 40, "Çay", "İçecekler"),
            new ProductInfo(21.35m, 40, "Zeytinyağı", "Yağlar")
        ]);

        svc.Recommend(Session()).Should().NotContain(r => r.Category != "İçecekler");
    }
}

/// <summary>
/// Mock'lu testler kuralları izole doğruluyor; bu tek test gerçek Postgres collation'ına
/// (<c>und-u-ks-level1</c>) karşı <c>FindProduct</c> + <c>GetAll</c> zincirinin gerçekten
/// birlikte çalıştığını kanıtlar — seed'de İçecekler kategorisi 3 ürün taşıyor (Çay/Bira/Kahve).
/// </summary>
[Collection("PostgresCatalog")]
public class RecommendationServiceIntegrationTests
{
    private readonly PostgresCatalogFixture _fixture;
    public RecommendationServiceIntegrationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void Recommend_RealCatalog_RecommendsSameCategoryProduct()
    {
        var understanding = Substitute.For<ICustomerUnderstandingService>();
        understanding.Build(Arg.Any<AgentSession>()).Returns(new CustomerUnderstanding(
            CustomerId: "1027", Persona: null, AdminNote: null, Traits: [],
            PreferredTone: "neutral", PreferredLanguage: "tr",
            ProductInterests: ["Çay"], TopIntents: [],
            AverageRating: null, RatingCount: 0, TotalSessions: 1, TotalTurns: 1,
            LastConsolidatedAt: null));

        var svc = new RecommendationService(understanding, _fixture.ProductRepo);
        var session = new AgentSession
        {
            SessionId = "s",
            State = new SessionState { AuthenticatedCustomerId = "1027", Sentiment = "neutral" }
        };

        var result = svc.Recommend(session);

        result.Should().NotBeEmpty();
        result.Should().OnlyContain(r => r.Category == "İçecekler");
        result.Should().NotContain(r => r.ProductName == "Çay", "zaten ilgilenilen ürün tekrar önerilmemeli");
    }
}
