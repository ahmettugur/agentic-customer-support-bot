using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Providers;

namespace CustomerSupportBot.Tests.Services;

public class CustomerContextProviderTests
{
    private readonly CustomerContextProvider _provider = new();

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
        ctx!.Should().Contain("CUST-1990");
        ctx.Should().Contain("Toplam sipariş");
    }

    [Fact]
    public async Task UnknownCustomer_NoOrdersBlock()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "CUST-NOTEXIST";
        var ctx = await _provider.GetContextAsync(session);
        ctx.Should().NotBeNull();
        ctx!.Should().Contain("Kayıtlı sipariş bulunamadı");
    }
}
