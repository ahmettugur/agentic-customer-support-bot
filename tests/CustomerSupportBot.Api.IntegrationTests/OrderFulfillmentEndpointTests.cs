// Sipariş durum uçları (fulfillment): yalnız Admin, durum kodları. Mantık OrderFulfillmentServiceTests'te.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class OrderFulfillmentEndpointTests : IClassFixture<OrderFulfillmentEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IOrderFulfillmentPort Fulfillment { get; } = Substitute.For<IOrderFulfillmentPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderFulfillmentPort>();
                services.AddSingleton(Fulfillment);
            });
        }
    }

    private readonly Factory _factory;

    private static OrderInfo Order(string status) => new()
    {
        CustomerId = "1001", Status = status, Lines = [new OrderLine("Kahve", 1)], Carrier = "Aras", TrackingNumber = "A1"
    };

    public OrderFulfillmentEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Fulfillment.ClearReceivedCalls();
        _factory.Fulfillment.Get("1044").Returns(Order(WellKnown.OrderStatuses.Processing));
        _factory.Fulfillment.MarkShippedAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(0) switch
            {
                "yok" => new OrderFulfillmentResult(OrderFulfillmentStatus.NotFound, "yok", null),
                "iptal" => new OrderFulfillmentResult(OrderFulfillmentStatus.InvalidTransition, "iptal", Order(WellKnown.OrderStatuses.Cancelled)),
                "uzun" => new OrderFulfillmentResult(OrderFulfillmentStatus.Invalid, "uzun", null, "Kargo firması en fazla 64 karakter olabilir."),
                var id => new OrderFulfillmentResult(OrderFulfillmentStatus.Updated, id, Order(WellKnown.OrderStatuses.Shipped))
            });
        _factory.Fulfillment.MarkDeliveredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new OrderFulfillmentResult(OrderFulfillmentStatus.Unchanged, ci.ArgAt<string>(0), Order(WellKnown.OrderStatuses.Delivered)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"u-{Guid.NewGuid():N}"[..12], "", role,
                role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Ship_PassesCarrierAndTracking_AndReturnsTheOrder()
    {
        var response = await ClientFor("Admin").PostAsJsonAsync("/orders/1044/shipment", new { carrier = "Aras", trackingNumber = "A1" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("changed").GetBoolean().Should().BeTrue();
        json.GetProperty("order").GetProperty("status").GetString().Should().Be(WellKnown.OrderStatuses.Shipped);
        await _factory.Fulfillment.Received(1).MarkShippedAsync("1044", "Aras", "A1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatusCodes()
    {
        var admin = ClientFor("Admin");

        (await admin.PostAsJsonAsync("/orders/yok/shipment", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var conflict = await admin.PostAsJsonAsync("/orders/iptal/shipment", new { }, Ct);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("currentStatus").GetString()
            .Should().Be(WellKnown.OrderStatuses.Cancelled);
        (await admin.PostAsJsonAsync("/orders/uzun/shipment", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var delivered = await admin.PostAsync("/orders/1044/delivery", null, Ct);
        delivered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await delivered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetOrder_ForTheAdminPanel()
    {
        var json = await ClientFor("Admin").GetFromJsonAsync<JsonElement>("/orders/1044", Ct);

        json.GetProperty("status").GetString().Should().Be(WellKnown.OrderStatuses.Processing);
        json.GetProperty("linesSummary").GetString().Should().Be("Kahve x1");
        (await ClientFor("Admin").GetAsync("/orders/9999", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyAdmin()
    {
        (await ClientFor("Agent").PostAsJsonAsync("/orders/1044/shipment", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer").GetAsync("/orders/1044", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
