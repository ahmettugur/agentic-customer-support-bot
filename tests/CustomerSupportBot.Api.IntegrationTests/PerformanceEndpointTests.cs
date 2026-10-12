// Performans ucu: yalnız Admin, tarih parametreleri, geçersiz aralıkta 400. Hesap PerformancePortServiceTests'te.

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

public class PerformanceEndpointTests : IClassFixture<PerformanceEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IPerformancePort Port { get; } = Substitute.For<IPerformancePort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPerformancePort>();
                services.AddSingleton(Port);
            });
        }
    }

    private readonly Factory _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PerformanceEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Port.ClearReceivedCalls();
        _factory.Port.GetAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var from = ci.ArgAt<DateTime?>(0);
                if (from is { Year: 1999 }) throw new ArgumentException("En fazla 92 günlük aralık seçilebilir.");
                return new PerformanceReport(from ?? DateTime.UtcNow.AddDays(-7), ci.ArgAt<DateTime?>(1) ?? DateTime.UtcNow,
                    new AiSummary(10, 0.8, 0.1, 0.2, 2500, 4, 0.25),
                    [new AiAgentRow("OrderAgent", 6, 3000, 8, 4, 1, 2, 1, 0.5)],
                    [new ToolRow("order_cancel_tool", "OrderAgent", 2, 1, 0.5, "X: yok")],
                    [new HumanAgentRow("agent-jdoe", "John Doe", 3, 2, 120, 1800, 1, 4.5, 2)],
                    []);
            });
    }

    private HttpClient ClientFor(string role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), "admin", "", role, role == "Agent" ? "agent-1" : null, true,
                DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_GetsTheReport_WithUtcRange()
    {
        var json = await ClientFor("Admin").GetFromJsonAsync<JsonElement>(
            "/analytics/performance?from=2026-10-01T00:00:00Z&to=2026-10-08T00:00:00Z", Ct);

        json.GetProperty("summary").GetProperty("traces").GetInt32().Should().Be(10);
        json.GetProperty("agents")[0].GetProperty("successRate").GetDouble().Should().Be(0.5);
        json.GetProperty("tools")[0].GetProperty("lastFailure").GetString().Should().Be("X: yok");
        json.GetProperty("humanAgents")[0].GetProperty("displayName").GetString().Should().Be("John Doe");
        await _factory.Port.Received(1).GetAsync(
            Arg.Is<DateTime?>(d => d == new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) && d!.Value.Kind == DateTimeKind.Utc),
            Arg.Is<DateTime?>(d => d == new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoRange_UsesDefaults()
    {
        (await ClientFor("Admin").GetAsync("/analytics/performance", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Port.Received(1).GetAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidRange_Is400()
    {
        var response = await ClientFor("Admin").GetAsync("/analytics/performance?from=1999-01-01T00:00:00Z", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Contain("92");
    }

    [Fact]
    public async Task OnlyAdmin()
    {
        (await ClientFor("Agent").GetAsync("/analytics/performance", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer").GetAsync("/analytics/performance", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
