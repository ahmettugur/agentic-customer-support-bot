using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Models.Memory;
using CustomerSupportBot.Api.Services.Personalization;
using CustomerSupportBot.Api.Services.Providers;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Api.Models.SessionState;

namespace CustomerSupportBot.Tests.Services.Providers;

public class CustomerProfileContextProviderTests
{
    private static CustomerProfileContextProvider Build(InMemoryCustomerProfileStore store)
        => new(store, NullLogger<CustomerProfileContextProvider>.Instance);

    [Fact]
    public async Task NoCustomerId_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        var provider = Build(store);

        var session = new AgentSession { SessionId = "s", State = new SessionState() };
        var ctx = await provider.GetContextAsync(session);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task NoProfileForCustomer_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        var provider = Build(store);

        var session = new AgentSession { SessionId = "s", State = new SessionState { CustomerId = "CUST-1" } };
        var ctx = await provider.GetContextAsync(session);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task EmptyProfile_ZeroTurns_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile { CustomerId = "CUST-1", TotalTurns = 0 });

        var provider = Build(store);
        var session = new AgentSession { SessionId = "s", State = new SessionState { CustomerId = "CUST-1" } };
        var ctx = await provider.GetContextAsync(session);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task PopulatedProfile_ReturnsContextBlockWithKeyFields()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile
        {
            CustomerId = "CUST-1990",
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
        var session = new AgentSession { SessionId = "s", State = new SessionState { CustomerId = "CUST-1990" } };
        var ctx = await provider.GetContextAsync(session);

        ctx.Should().NotBeNull();
        ctx.Should().Contain("👤 Müşteri Profili")
                  .And.Contain("CUST-1990")
                  .And.Contain("Dell XPS 15 müşterisi")
                  .And.Contain("VIP müşteri")
                  .And.Contain("order_inquiry")
                  .And.Match("*ortalama puan: 4*5*"); // (4+5+3)/3 = 4.0; culture-agnostic
    }

    [Fact]
    public void Order_PutsProfileAfterCustomerContextButBeforeSemanticMemory()
    {
        var provider = Build(new InMemoryCustomerProfileStore());
        provider.Order.Should().Be(6);
        provider.Name.Should().Be("CustomerProfile");
    }
}
