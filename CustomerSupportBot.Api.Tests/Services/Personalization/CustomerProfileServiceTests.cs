using RedisOptions = CustomerSupportBot.Adapters.Redis.RedisOptions;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Personalization;

[Collection("PostgresCatalog")]
public class CustomerProfileServiceTests
{
    private readonly PostgresCatalogFixture _fixture;

    public CustomerProfileServiceTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private (CustomerProfileService Service, InMemoryCustomerProfileStore Store, FakeChat Chat) Build()
    {
        var store = new InMemoryCustomerProfileStore();
        var chat = new FakeChat();
        var lockOptions = Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 });
        var distributedLock = new InMemoryDistributedLock(lockOptions);
        var svc = new CustomerProfileService(store, chat, distributedLock, _fixture.ProductRepo, NullLogger<CustomerProfileService>.Instance);
        return (svc, store, chat);
    }

    [Fact]
    public async Task RecordInteraction_BlankCustomerId_NoOp()
    {
        var (svc, store, _) = Build();
        var p = await svc.RecordInteractionAsync(null, "merhaba", "ok", "greeting", ct: TestContext.Current.CancellationToken);
        p.Should().BeNull();
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task RecordInteraction_BlankQuery_NoOp()
    {
        var (svc, store, _) = Build();
        var p = await svc.RecordInteractionAsync("CUST-1", "", "ok", "x", ct: TestContext.Current.CancellationToken);
        p.Should().BeNull();
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task RecordInteraction_FirstCall_CreatesProfileAndCounts()
    {
        var (svc, store, _) = Build();
        var p = await svc.RecordInteractionAsync("CUST-1", "Laptop stokta var mı?", "Evet 10 adet.", "product_inquiry", isNewSession: true, ct: TestContext.Current.CancellationToken);

        p.Should().NotBeNull();
        p.TotalSessions.Should().Be(1);
        p.TotalTurns.Should().Be(1);
        p.IntentFrequency["product_inquiry"].Should().Be(1);
        p.PreferredLanguage.Should().Be("tr");
        p.ProductInterests.Should().Contain("Laptop");
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task RecordInteraction_AccumulatesIntentFrequency()
    {
        var (svc, _, _) = Build();
        await svc.RecordInteractionAsync("CUST-1", "ORD-1 nerede?", "Yolda.", "order_inquiry", ct: TestContext.Current.CancellationToken);
        await svc.RecordInteractionAsync("CUST-1", "ORD-1 nerede?", "Yolda.", "order_inquiry", ct: TestContext.Current.CancellationToken);
        var p = await svc.RecordInteractionAsync("CUST-1", "şikayet etmek istiyorum", "Tamam.", "complaint", ct: TestContext.Current.CancellationToken);

        p!.IntentFrequency["order_inquiry"].Should().Be(2);
        p.IntentFrequency["complaint"].Should().Be(1);
        p.TotalTurns.Should().Be(3);
        p.TotalSessions.Should().Be(0); // isNewSession default false
    }

    [Fact]
    public async Task RecordInteraction_DedupesProductsAndKeepsNewestFirst()
    {
        // ExtractProductMentions artık katalog-tabanlı: metni IProductCatalogRepository'deki
        // gerçek ürün adlarıyla eşleştiriyor (eskiden marka kalıbı çıkarımı yapıyordu).
        // Bu yüzden sorgular seed'de gerçekten var olan ürünlere referans vermeli.
        var (svc, _, _) = Build();
        await svc.RecordInteractionAsync("CUST-1", "Laptop sorgula", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);
        await svc.RecordInteractionAsync("CUST-1", "Tablet fiyat", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);
        var p = await svc.RecordInteractionAsync("CUST-1", "Laptop yeniden sor", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);

        // En son söz edilen Laptop başa gelmeli
        p!.ProductInterests.Should().StartWith(new[] { "Laptop", "Tablet" });
        p.ProductInterests.Distinct().Should().HaveCount(p.ProductInterests.Count);
    }

    [Fact]
    public async Task RecordInteraction_DetectsEnglishLanguage()
    {
        var (svc, _, _) = Build();
        var p = await svc.RecordInteractionAsync("CUST-1", "Hello, where is my order?", "On the way.", "order_inquiry", ct: TestContext.Current.CancellationToken);
        p!.PreferredLanguage.Should().Be("en");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    [InlineData(6, false)]
    public async Task RecordInteraction_AppendsValidRatings(int rating, bool shouldAppend)
    {
        var (svc, _, _) = Build();
        var p = await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", rating: rating, ct: TestContext.Current.CancellationToken);
        if (shouldAppend) p!.RecentRatings.Should().Equal(new[] { rating });
        else p!.RecentRatings.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsolidateAsync_NoProfile_ReturnsNull()
    {
        var (svc, _, _) = Build();
        var p = await svc.ConsolidateAsync("CUST-NOPE", TestContext.Current.CancellationToken);
        p.Should().BeNull();
    }

    [Fact]
    public async Task ConsolidateAsync_LlmReturnsJson_UpdatesSummaryAndTone()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "ORD-1 nerede?", "Yolda.", "order_inquiry", ct: TestContext.Current.CancellationToken);
        chat.Reply = "{\"summary\":\"Sık sipariş takibi yapan müşteri.\",\"preferredTone\":\"concise\"}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Summary.Should().Be("Sık sipariş takibi yapan müşteri.");
        p.PreferredTone.Should().Be("concise");
        p.LastConsolidatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ConsolidateAsync_LlmGarbage_KeepsExistingProfile()
    {
        var (svc, _, chat) = Build();
        var existing = (await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken))!;
        existing.Summary = "ÖNCEKİ";
        chat.Reply = "no json here at all";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Summary.Should().Be("ÖNCEKİ");
    }

    [Fact]
    public async Task ConsolidateAsync_LlmThrows_DoesNotPropagate()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);
        chat.ThrowOnCall = new InvalidOperationException("boom");

        var act = async () => await svc.ConsolidateAsync("CUST-1");
        await act.Should().NotThrowAsync();
    }

    // ═══ Traits — confidence + source ═══

    [Fact]
    public async Task ConsolidateAsync_LlmReturnsTraits_PopulatesConfidenceAndSource()
    {
        var (svc, _, chat) = Build();
        var existing = (await svc.RecordInteractionAsync("CUST-1", "en pahalı olan hangisi", "X ürünü.", "product_inquiry",
            ct: TestContext.Current.CancellationToken))!;
        chat.Reply = "{\"summary\":\"x\",\"preferredTone\":\"neutral\"," +
                     "\"traits\":[{\"claim\":\"Fiyat hassasiyeti düşük\",\"confidence\":0.8}]}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits.Should().HaveCount(1);
        p.Traits[0].Claim.Should().Be("Fiyat hassasiyeti düşük");
        p.Traits[0].Confidence.Should().Be(0.8);
        p.Traits[0].Source.Should().Contain("CUST-1").And.Contain($"turn{existing.TotalTurns}");
    }

    /// <summary>
    /// Traits BİRİKMEZ — her consolidate çağrısı baştan üretir. Aksi halde geçersiz hâle
    /// gelmiş eski bir iddia sonsuza kadar profilde kalır ve yenisiyle çelişirdi.
    /// </summary>
    [Fact]
    public async Task ConsolidateAsync_SecondCall_ReplacesTraits_DoesNotAccumulate()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);

        chat.Reply = "{\"summary\":\"x\",\"preferredTone\":\"neutral\",\"traits\":[{\"claim\":\"A\",\"confidence\":0.5}]}";
        await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        chat.Reply = "{\"summary\":\"x\",\"preferredTone\":\"neutral\",\"traits\":[{\"claim\":\"B\",\"confidence\":0.5}]}";
        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits.Should().ContainSingle(t => t.Claim == "B");
        p.Traits.Should().NotContain(t => t.Claim == "A");
    }

    [Theory]
    [InlineData(1.5, 1.0)]   // aralık üstü → kırpılır
    [InlineData(-0.3, 0.0)]  // aralık altı → kırpılır
    public async Task ConsolidateAsync_ConfidenceOutOfRange_IsClamped(double raw, double expected)
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);
        chat.Reply = $"{{\"summary\":\"x\",\"preferredTone\":\"neutral\"," +
                     $"\"traits\":[{{\"claim\":\"X\",\"confidence\":{raw.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]}}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits[0].Confidence.Should().Be(expected);
    }

    /// <summary>Boş claim'li girişler sessizce atlanır — LLM'in ürettiği çöpü profile taşımaz.</summary>
    [Fact]
    public async Task ConsolidateAsync_TraitWithBlankClaim_IsSkipped()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);
        chat.Reply = "{\"summary\":\"x\",\"preferredTone\":\"neutral\"," +
                     "\"traits\":[{\"claim\":\"\",\"confidence\":0.5},{\"claim\":\"Geçerli\",\"confidence\":0.5}]}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits.Should().HaveCount(1, "boş claim atlanmalı, yalnızca geçerli olan kalmalı");
        p.Traits[0].Claim.Should().Be("Geçerli");
    }

    [Fact]
    public async Task ConsolidateAsync_MoreThanMaxTraits_IsCapped()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);
        var traits = string.Join(",", Enumerable.Range(1, 8).Select(i => $"{{\"claim\":\"T{i}\",\"confidence\":0.5}}"));
        chat.Reply = $"{{\"summary\":\"x\",\"preferredTone\":\"neutral\",\"traits\":[{traits}]}}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits.Should().HaveCount(5);
    }

    /// <summary>traits alanı hiç yoksa (eski/uyumsuz LLM çıktısı) boş liste döner, çökmez.</summary>
    [Fact]
    public async Task ConsolidateAsync_NoTraitsField_ReturnsEmptyList()
    {
        var (svc, _, chat) = Build();
        await svc.RecordInteractionAsync("CUST-1", "test", "ok", "x", ct: TestContext.Current.CancellationToken);
        chat.Reply = "{\"summary\":\"x\",\"preferredTone\":\"neutral\"}";

        var p = await svc.ConsolidateAsync("CUST-1", TestContext.Current.CancellationToken);

        p!.Traits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("merhaba ne yapıyorsun", true)]
    [InlineData("Hello there", false)]
    [InlineData("siparişim nerede", true)]      // ş karakteri
    [InlineData("where is my order please", false)]
    public void LooksTurkish_Detects(string text, bool expected)
    {
        CustomerProfileService.LooksTurkish(text).Should().Be(expected);
    }

    // ─── Test helpers ───
    private sealed class FakeChat : IGeneralChatClient
    {
        public string Reply { get; set; } = "{}";
        public Exception? ThrowOnCall { get; set; }

        public Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)
        {
            if (ThrowOnCall != null) throw ThrowOnCall;
            return Task.FromResult(Reply);
        }
    }
}
