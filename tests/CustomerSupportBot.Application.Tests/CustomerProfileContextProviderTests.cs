using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Application.Tests;

public class CustomerProfileContextProviderTests
{
    // Understanding sentezi burada MOCK'lanmıyor — gerçek CustomerUnderstandingService
    // üzerinden çalıştırılıyor ki bu testler sentez + render zincirinin TAMAMINI kapsasın.
    private static CustomerProfileContextProvider Build(InMemoryCustomerProfileStore store)
        => new(new CustomerUnderstandingService(store), NullLogger<CustomerProfileContextProvider>.Instance);

    [Fact]
    public async Task NoCustomerId_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        var provider = Build(store);

        var session = new AgentSession { SessionId = "s", State = new SessionState() };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task NoProfileForCustomer_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        var provider = Build(store);

        var session = new AgentSession { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = "1001" } };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task EmptyProfile_ZeroTurns_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile { CustomerId = "1001", TotalTurns = 0 });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = "1001" } };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task PopulatedProfile_ReturnsContextBlockWithKeyFields()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            Summary = "Dell XPS 15 müşterisi",
            PreferredLanguage = "tr",
            PreferredTone = "concise",
            TotalSessions = 3,
            TotalTurns = 12,
            IntentFrequency = new Dictionary<string, int>
            {
                ["order_inquiry"] = 6,
                ["complaint"] = 2,
                ["product_inquiry"] = 4
            },
            ProductInterests = new List<string> { "Dell XPS 15", "Apple iPhone 15 Pro" },
            RecentRatings = new List<int> { 4, 5, 3 },
            AdminNote = "VIP müşteri"
        });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = "1027" } };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");

        ctx.Should().NotBeNull();
        ctx.Should().Contain("Profili")
                  .And.Contain("1027")
                  .And.Contain("Dell XPS 15")
                  .And.Contain("VIP")
                  .And.Contain("order_inquiry")
                  .And.Match("*ortalama puan: 4*5*"); // (4+5+3)/3 = 4.0; culture-agnostic
    }

    [Fact]
    public async Task LlmExtractedCustomerId_DoesNotLeakAnotherCustomersProfile()
    {
        // Profil bloğu admin notu ve geçmiş özeti gibi hassas alanlar taşır — kullanıcının
        // metinde iddia ettiği kimlik (State.CustomerId) ile ASLA çekilmemeli.
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1008",
            Summary = "başka müşterinin özeti",
            AdminNote = "GİZLİ ADMIN NOTU",
            TotalTurns = 10
        });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { CustomerId = "1008" } };
        var ctx = await provider.GetContextAsync(session, "profilimi göster");

        ctx.Should().BeNull("LLM'in metinden çıkardığı kimlik profil erişimi için kullanılmamalı");
    }

    /// <summary>
    /// Trait'ler confidence'la BİRLİKTE görünmeli — çıplak iddia, model tarafından olgu
    /// sanılabilir. "%80" formatı hem okunur hem de bunun bir tahmin olduğunu işaret eder.
    /// </summary>
    [Fact]
    public async Task PopulatedProfile_WithTraits_IncludesConfidence()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            TotalTurns = 5,
            Traits =
            [
                new InferredTrait("Fiyat hassasiyeti yüksek", 0.8, "consolidate:1027@turn5", DateTime.UtcNow),
                new InferredTrait("Teknik detaylara önem veriyor", 0.4, "consolidate:1027@turn5", DateTime.UtcNow)
            ]
        });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = "1027" } };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");

        ctx.Should().NotBeNull();
        ctx.Should().Contain("Fiyat hassasiyeti yüksek").And.Contain("%80");
        ctx.Should().Contain("Teknik detaylara önem veriyor").And.Contain("%40");
    }

    [Fact]
    public async Task PopulatedProfile_NoTraits_OmitsTraitsSection()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile { CustomerId = "1027", TotalTurns = 5 });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = "1027" } };
        var ctx = await provider.GetContextAsync(session, "test sorgusu");

        ctx.Should().NotContain("Davranışsal gözlemler");
    }

    [Fact]
    public void Order_PutsProfileAfterCustomerContextButBeforeSemanticMemory()
    {
        var provider = Build(new InMemoryCustomerProfileStore());
        provider.Order.Should().Be(6);
        provider.Name.Should().Be("CustomerProfile");
    }
}
