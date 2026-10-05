// Konuşma arama ucu: yalnız Admin, parametrelerin servise geçişi, 400. Mantık ConversationSearchServiceTests'te.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ConversationSearchEndpointTests : IClassFixture<ConversationSearchEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IConversationSearchPort Search { get; } = Substitute.For<IConversationSearchPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConversationSearchPort>();
                services.AddSingleton(Search);
            });
        }
    }

    private readonly Factory _factory;

    public ConversationSearchEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Search.ClearReceivedCalls();
        _factory.Search.SearchAsync(Arg.Any<ConversationSearchQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
            ci.Arg<ConversationSearchQuery>().Text == "x"
                ? new ConversationSearchResult(null, "Arama metni en az 2 karakter olmalı.")
                : new ConversationSearchResult(new ConversationSearchPage(
                    [new ConversationSearchHit("s-1", "1001", DateTime.UtcNow, DateTime.UtcNow, 3, "İade istiyorum", 0, 4, ["resolved"], ["iade"])],
                    1, 25, false), null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"u-{Guid.NewGuid():N}"[..12], "", role,
                role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_Searches_WithAllParameters()
    {
        var url = "/conversations/search?q=iade&customerId=1001&from=2026-10-01T21:00:00Z&to=2026-10-05T21:00:00Z&reason=resolved&tag=iade&page=2";

        var json = await ClientFor("Admin").GetFromJsonAsync<JsonElement>(url, Ct);

        json.GetProperty("items")[0].GetProperty("snippet").GetString().Should().Be("İade istiyorum");
        json.GetProperty("items")[0].GetProperty("highlightLength").GetInt32().Should().Be(4);
        await _factory.Search.Received(1).SearchAsync(Arg.Is<ConversationSearchQuery>(q =>
            q.Text == "iade" && q.CustomerId == "1001" && q.Reason == "resolved" && q.Tag == "iade" && q.Page == 2 &&
            q.From == new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc) && q.From!.Value.Kind == DateTimeKind.Utc &&
            q.To == new DateTime(2026, 10, 5, 21, 0, 0, DateTimeKind.Utc)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidQuery_Is400()
    {
        var response = await ClientFor("Admin").GetAsync("/conversations/search?q=x", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Contain("en az 2");
    }

    [Fact]
    public async Task OnlyAdmin()
    {
        (await ClientFor("Agent").GetAsync("/conversations/search", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/conversations/search", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
