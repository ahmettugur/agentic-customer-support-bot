using CustomerSupportBot.Api.Services.Personalization;
using CustomerSupportBot.Api.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Personalization;

public class CustomerProfileServiceTests
{
    private static (CustomerProfileService Service, InMemoryCustomerProfileStore Store, FakeChat Chat) Build()
    {
        var store = new InMemoryCustomerProfileStore();
        var chat = new FakeChat();
        var lockOptions = Options.Create(new Api.Models.RedisOptions { DefaultLockTimeoutSeconds = 10 });
        var distributedLock = new InMemoryDistributedLock(lockOptions);
        var svc = new CustomerProfileService(store, chat, distributedLock, new InMemoryProductCatalogAdapter(), NullLogger<CustomerProfileService>.Instance);
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
        var p = await svc.RecordInteractionAsync("CUST-1", "Dell XPS 15 stokta var mı?", "Evet 10 adet.", "product_inquiry", isNewSession: true, ct: TestContext.Current.CancellationToken);

        p.Should().NotBeNull();
        p!.TotalSessions.Should().Be(1);
        p.TotalTurns.Should().Be(1);
        p.IntentFrequency["product_inquiry"].Should().Be(1);
        p.PreferredLanguage.Should().Be("tr");
        p.ProductInterests.Should().Contain("Dell XPS 15");
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
        var (svc, _, _) = Build();
        await svc.RecordInteractionAsync("CUST-1", "Dell XPS 15 sorgula", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);
        await svc.RecordInteractionAsync("CUST-1", "Apple iPhone 15 Pro fiyat", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);
        var p = await svc.RecordInteractionAsync("CUST-1", "Dell XPS 15 yeniden sor", "ok", "product_inquiry", ct: TestContext.Current.CancellationToken);

        // En son söz edilen Dell başa gelmeli
        p!.ProductInterests.Should().StartWith(new[] { "Dell XPS 15", "Apple iPhone 15 Pro" });
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
    private sealed class FakeChat : IChatClient
    {
        public string Reply { get; set; } = "{}";
        public Exception? ThrowOnCall { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCall != null) throw ThrowOnCall;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
        }

#pragma warning disable CS1998
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
#pragma warning restore CS1998

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
