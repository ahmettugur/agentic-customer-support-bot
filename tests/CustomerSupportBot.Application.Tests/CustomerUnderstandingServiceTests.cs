// Tests/Services/Personalization/CustomerUnderstandingServiceTests.cs
//
// CustomerUnderstandingService, CustomerProfile'ı tek bir sentezlenmiş görünüme çevirir.
// Bu testler iki şeyi doğrular: (1) null-semantiği eski CustomerProfileContextProvider'ın
// doğrudan sahip olduğu davranışla BİREBİR aynı kalmalı (regresyon riski — bu davranış artık
// iki tüketici arasında paylaşılıyor), (2) sıralama/sınırlama kuralları (top-N) doğru çalışıyor.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Application.Tests;

public class CustomerUnderstandingServiceTests
{
    private static CustomerUnderstandingService Build(out InMemoryCustomerProfileStore store)
    {
        store = new InMemoryCustomerProfileStore();
        return new CustomerUnderstandingService(store);
    }

    private static AgentSession Session(string? customerId) =>
        new() { SessionId = "s", State = new SessionState { AuthenticatedCustomerId = customerId } };

    // ═══ Null-semantiği — eski provider'ın üç ayrı null-dönüş yolunun karşılığı ═══

    [Fact]
    public void Build_NoAuthenticatedCustomerId_ReturnsNull()
    {
        var svc = Build(out _);
        svc.Build(Session(null)).Should().BeNull();
    }

    [Fact]
    public void Build_NoProfileForCustomer_ReturnsNull()
    {
        var svc = Build(out _);
        svc.Build(Session("1001")).Should().BeNull();
    }

    [Fact]
    public void Build_ProfileWithZeroTurns_ReturnsNull()
    {
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile { CustomerId = "1001", TotalTurns = 0 });
        svc.Build(Session("1001")).Should().BeNull();
    }

    /// <summary>
    /// LLM'in metinden çıkardığı kimlik (State.CustomerId) ASLA kullanılmamalı — başkasının
    /// profilini (admin notu dahil) sızdırırdı. Bu, eski provider'ın en kritik testiydi;
    /// sentez servise taşınırken kaybolmadığını doğrular.
    /// </summary>
    [Fact]
    public void Build_UsesOnlyAuthenticatedCustomerId_NotLlmExtractedOne()
    {
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile { CustomerId = "1008", AdminNote = "GİZLİ", TotalTurns = 10 });

        var session = new AgentSession
        {
            SessionId = "s",
            State = new SessionState { CustomerId = "1008", AuthenticatedCustomerId = null }
        };

        svc.Build(session).Should().BeNull();
    }

    // ═══ Sentez içeriği ═══

    [Fact]
    public void Build_PopulatedProfile_MapsAllFields()
    {
        var svc = Build(out var store);
        var consolidatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            Summary = "Dell XPS 15 müşterisi",
            AdminNote = "VIP",
            PreferredLanguage = "tr",
            PreferredTone = "concise",
            TotalSessions = 3,
            TotalTurns = 12,
            RecentRatings = [4, 5, 3],
            LastConsolidatedAt = consolidatedAt,
            Traits = [new InferredTrait("Fiyat hassasiyeti düşük", 0.8, "consolidate:1027@turn12", consolidatedAt)]
        });

        var u = svc.Build(Session("1027"));

        u.Should().NotBeNull();
        u!.CustomerId.Should().Be("1027");
        u.Persona.Should().Be("Dell XPS 15 müşterisi");
        u.AdminNote.Should().Be("VIP");
        u.AverageRating.Should().Be(4.0);
        u.RatingCount.Should().Be(3);
        u.LastConsolidatedAt.Should().Be(consolidatedAt);
        u.Traits.Should().ContainSingle(t => t.Claim == "Fiyat hassasiyeti düşük");
    }

    [Fact]
    public void Build_NoRatings_AverageRatingIsNull()
    {
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile { CustomerId = "1027", TotalTurns = 1 });

        svc.Build(Session("1027"))!.AverageRating.Should().BeNull();
    }

    [Fact]
    public void Build_NeverConsolidated_PersonaAndTraitsAreEmpty_ButProfileStillReturned()
    {
        // Heuristik alanlar (IntentFrequency, ProductInterests) her turda güncellenir ve
        // consolidate'e bağlı değildir — hiç consolidate edilmemiş bir profil yine de
        // anlamlı bir Understanding üretmeli, yalnızca Persona/Traits boş kalır.
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            TotalTurns = 2,
            ProductInterests = ["Laptop"],
            IntentFrequency = new Dictionary<string, int> { ["product_inquiry"] = 2 }
        });

        var u = svc.Build(Session("1027"));

        u.Should().NotBeNull();
        u!.Persona.Should().BeNull();
        u.Traits.Should().BeEmpty();
        u.LastConsolidatedAt.Should().BeNull();
        u.ProductInterests.Should().Contain("Laptop");
    }

    // ═══ Top-N sınırlama ve sıralama ═══

    [Fact]
    public void Build_MoreThanFiveProductInterests_TakesFirstFive()
    {
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            TotalTurns = 1,
            ProductInterests = ["A", "B", "C", "D", "E", "F", "G"]
        });

        svc.Build(Session("1027"))!.ProductInterests.Should().Equal("A", "B", "C", "D", "E");
    }

    [Fact]
    public void Build_TopIntents_OrderedByFrequencyDescending_CappedAtThree()
    {
        var svc = Build(out var store);
        store.Upsert(new CustomerProfile
        {
            CustomerId = "1027",
            TotalTurns = 1,
            IntentFrequency = new Dictionary<string, int>
            {
                ["a"] = 1, ["b"] = 5, ["c"] = 3, ["d"] = 4, ["e"] = 2
            }
        });

        var u = svc.Build(Session("1027"));

        u!.TopIntents.Should().HaveCount(3);
        u.TopIntents.Select(t => t.Intent).Should().Equal("b", "d", "c");
    }
}
