using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Api.Tests.Services.Providers;

public class ProductRecommendationContextProviderTests
{
    private static ProductRecommendationContextProvider Build(IRecommendationService recommendations)
        => new(recommendations, NullLogger<ProductRecommendationContextProvider>.Instance);

    private static AgentSession Session() =>
        new() { SessionId = "s", State = new SessionState() };

    [Fact]
    public async Task NoRecommendations_ReturnsNull()
    {
        var recommendations = Substitute.For<IRecommendationService>();
        recommendations.Recommend(Arg.Any<AgentSession>(), Arg.Any<int>()).Returns([]);

        var provider = Build(recommendations);
        var ctx = await provider.GetContextAsync(Session(), "test sorgusu");

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task WithRecommendations_RendersAsOptionalNotInstruction()
    {
        var recommendations = Substitute.For<IRecommendationService>();
        recommendations.Recommend(Arg.Any<AgentSession>(), Arg.Any<int>()).Returns(
            [new ProductRecommendation("Bira", "'Çay' ile aynı kategoride (İçecekler)", "İçecekler")]);

        var provider = Build(recommendations);
        var ctx = await provider.GetContextAsync(Session(), "test sorgusu");

        ctx.Should().NotBeNull();
        ctx.Should().Contain("Bira");
        // Bir talimat değil bir öneri olduğu açıkça yazmalı — model bunu zorunluluk sanmamalı.
        ctx.Should().Contain("isteğe bağlı").And.Contain("zorlama");
    }

    [Fact]
    public void Order_IsBetweenSemanticMemoryAndCustomerContext()
    {
        var provider = Build(Substitute.For<IRecommendationService>());
        provider.Order.Should().Be(8);
    }
}
