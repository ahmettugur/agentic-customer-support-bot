using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services.Providers;
using CustomerSupportBot.Api.Tests.Helpers;

namespace CustomerSupportBot.Api.Tests.Services;

public class CustomerContextProviderTests
{
    private readonly CustomerContextProvider _provider = new(
        TestFactory.CreateOrders(),
        TestFactory.CreateComplaints());

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
        var ctx = await _provider.GetContextAsync(session);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task KnownCustomer_IncludesOrdersAndComplaints()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "CUST-1990";
        var ctx = await _provider.GetContextAsync(session);
        ctx.Should().NotBeNull();
        ctx.Should().Contain("CUST-1990");
        ctx.Should().Contain("Toplam sipari�");
    }

    [Fact]
    public async Task UnknownCustomer_NoOrdersBlock()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "CUST-NOTEXIST";
        var ctx = await _provider.GetContextAsync(session);
        ctx.Should().NotBeNull();
        ctx.Should().Contain("Kay�tl� sipari� bulunamad�");
    }
}
