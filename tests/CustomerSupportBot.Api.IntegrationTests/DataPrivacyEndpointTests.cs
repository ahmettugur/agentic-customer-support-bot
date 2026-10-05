// Kişisel veri uçları: müşteri kendi verisini indirir/siler; yönetici bir müşteri adına (KVKK başvurusu).

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

public class DataPrivacyEndpointTests : IClassFixture<DataPrivacyEndpointTests.Factory>
{
    /// <summary>Uç katmanı sınanır; silme/dışa aktarma mantığı DataPrivacyServiceTests'te.</summary>
    public sealed class Factory : TestWebApplicationFactory
    {
        public IDataPrivacyPort Privacy { get; } = Substitute.For<IDataPrivacyPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataPrivacyPort>();
                services.AddSingleton(Privacy);
            });
        }
    }

    private readonly Factory _factory;
    public DataPrivacyEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Privacy.ClearReceivedCalls();
        _factory.Privacy.ExportCustomerDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new CustomerDataExport(ci.Arg<string>(), DateTime.UtcNow, null, [], [], [], [], []));
        _factory.Privacy.EraseCustomerDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ErasureResult(ci.Arg<string>(), 2, new Dictionary<string, int> { ["sessions"] = 2 }));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role, string? customerId = null)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"u-{Guid.NewGuid():N}", "", role,
                role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, customerId),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Customer_ExportsOwnData_AsADownload()
    {
        var resp = await ClientFor("Customer", "1001").GetAsync("/customer/data/export", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentDisposition?.DispositionType.Should().Be("attachment");
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("customerId").GetString().Should().Be("1001");
        await _factory.Privacy.Received(1).ExportCustomerDataAsync("1001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Customer_Delete_RequiresConfirmation()
    {
        var resp = await ClientFor("Customer", "1001").DeleteAsync("/customer/data", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be("confirmation_required");
        await _factory.Privacy.DidNotReceive().EraseCustomerDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Customer_Delete_ErasesOnlyTheirOwnData()
    {
        var resp = await ClientFor("Customer", "1001").DeleteAsync("/customer/data?confirm=true", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("sessionsErased").GetInt32().Should().Be(2);
        await _factory.Privacy.Received(1).EraseCustomerDataAsync("1001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_PartialFailure_Is500_WithTheFailedStores()
    {
        _factory.Privacy.EraseCustomerDataAsync("3003", Arg.Any<CancellationToken>())
            .Returns<ErasureResult>(_ => throw new DataErasureException(["escalations"]));

        var resp = await ClientFor("Customer", "3003").DeleteAsync("/customer/data?confirm=true", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("erasure_incomplete");
        body.GetProperty("failedStores")[0].GetString().Should().Be("escalations");
    }

    [Fact]
    public async Task Admin_CanExportAndEraseForACustomer()
    {
        var admin = ClientFor("Admin");

        (await admin.GetAsync("/customers/1001/data/export", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync("/customers/1001/data?confirm=true", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.Privacy.Received(1).ExportCustomerDataAsync("1001", Arg.Any<CancellationToken>());
        await _factory.Privacy.Received(1).EraseCustomerDataAsync("1001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyAdmins_CanUseTheCustomerScopedRoutes()
    {
        (await ClientFor("Agent").DeleteAsync("/customers/1001/data?confirm=true", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer", "2002").GetAsync("/customers/1001/data/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/customer/data/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.Privacy.DidNotReceive().EraseCustomerDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
