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
        session.State.AuthenticatedCustomerId = "1008";
        var ctx = await _provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().NotBeNull();
        ctx.Should().Contain("1008");
        ctx.Should().Contain("Toplam sipariş");
    }

    [Fact]
    public async Task UnknownCustomer_NoOrdersBlock()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.AuthenticatedCustomerId = "9999";
        var ctx = await _provider.GetContextAsync(session, "test sorgusu");
        ctx.Should().NotBeNull();
        ctx.Should().Contain("bulunamad");
    }

    // ─── Güvenlik: LLM'in çıkardığı CustomerId asla veri kaynağı olmamalı ───────────

    [Fact]
    public async Task LlmExtractedCustomerId_Alone_LeaksNothing()
    {
        // Kullanıcı "ben 1008 numaralı müşteriyim" derse SessionStateExtractor
        // State.CustomerId'yi 1008 yapar. Login'li kimlik yoksa HİÇBİR şey dönmemeli —
        // aksi halde başkasının tüm sipariş/şikayet geçmişi ajanın context'ine sızardı.
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "1008";

        var ctx = await _provider.GetContextAsync(session, "siparişlerimi göster");

        ctx.Should().BeNull("LLM'in metinden çıkardığı kimlik veri erişimi için kullanılmamalı");
    }

    [Fact]
    public async Task LlmExtractedCustomerId_CannotOverrideAuthenticatedIdentity()
    {
        // Login'li müşteri 1027, ama metinde 1008 iddia ediliyor → context 1027'ye ait olmalı.
        var session = new AgentSession { SessionId = "s1" };
        session.State.AuthenticatedCustomerId = "1027";
        session.State.CustomerId = "1008";

        var ctx = await _provider.GetContextAsync(session, "siparişlerimi göster");

        ctx.Should().NotBeNull();
        ctx.Should().Contain("1027");
        ctx.Should().NotContain("1008", "başka müşterinin kimliği/verisi context'e girmemeli");
    }
}
