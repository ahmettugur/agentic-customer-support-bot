using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Tests.Infrastructure;

namespace CustomerSupportBot.Api.Tests.Services;

[Collection("PostgresCatalog")]
public class CustomerContextProviderTests
{
    private readonly CustomerContextProvider _provider;

    public CustomerContextProviderTests(PostgresCatalogFixture fixture)
    {
        _provider = new CustomerContextProvider(fixture.OrderRepo, fixture.ComplaintRepo);
    }

    [Fact]
    public void NameAndOrder_AreCorrect()
    {
        _provider.Name.Should().Be("CustomerContext");
        _provider.Order.Should().Be(10);
    }

    [Fact]
    public async Task NoCustomerId_ReturnsNull()
    {
        var session = new AgentSession { SessionId = "s1" };
        var ctx = await _provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task KnownCustomer_IncludesOrdersAndComplaints()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "1008";
        var ctx = await _provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().NotBeNull();
        ctx.Should().Contain("1008");
        ctx.Should().Contain("Toplam sipariş");
    }

    [Fact]
    public async Task UnknownCustomer_NoOrdersBlock()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "9999";
        var ctx = await _provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().NotBeNull();
        ctx.Should().Contain("bulunamad");
    }
}
