// MCP sunucusu — GERÇEK MCP istemcisiyle (Streamable HTTP), uygulamanın üretim kompozisyonuna karşı.
//
// Güvenlik özellikleri burada ölçülür, varsayılmaz: kimliksiz çağrı reddedilir, Partner token'ı müşteri verisine ULAŞAMAZ,
// özne token'ı tek müşteriye kilitlidir (kimlik ambient bağlamdan gelir, argümandan değil), yazma tool'u hiçbir kümede yok.

using System.Net;
using System.Net.Http.Headers;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CustomerSupportBot.Api.IntegrationTests;

public sealed class McpFactory : A2AConformanceFactory
{
    public System.Collections.Concurrent.ConcurrentQueue<string> LastOrderCustomerIds { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Mcp:Enabled", "true");

        var tools = Substitute.For<ICustomerSupportToolsService>();
        tools.GetLastOrderTool(Arg.Any<string>()).Returns(call =>
        {
            var customerId = call.Arg<string>();
            LastOrderCustomerIds.Enqueue(customerId);
            return new ToolResult { Success = true, Message = $"son sipariş sahibi: {customerId}" };
        });
        tools.ProductListTool(Arg.Any<string?>()).Returns(new ToolResult { Success = true, Message = "Kategori A, Kategori B" });
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton(tools)));
    }
}

public sealed class McpDisabledFactory : TestWebApplicationFactory;

public class McpEndpointTests : IClassFixture<McpFactory>
{
    private readonly McpFactory _factory;
    public McpEndpointTests(McpFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Token(string role, string? customerId = null, string partnerId = "test-caller")
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var id = role == A2ARoles.Subject ? A2ASubjectIdentity.BuildId(partnerId, customerId!) : partnerId;
        var user = new UserInfo(Id: id, Username: $"a2a:{partnerId}", PasswordHash: "", Role: role, LinkedAgentId: null,
            IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null, LinkedCustomerId: customerId);
        return provider.GenerateAccessToken(user, DateTime.UtcNow).Token;
    }

    private HttpClient Http(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<McpClient> ConnectAsync(string path, string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost" + path), TransportMode = HttpTransportMode.StreamableHttp },
            Http(token), ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    // ═══ kimlik doğrulama ═══

    [Theory]
    [InlineData("/mcp/catalog")]
    [InlineData("/mcp/customer")]
    public async Task Anonymous_IsRejected(string path)
    {
        var response = await Http(null).PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PartnerToken_CannotReachCustomerData()
    {
        var response = await Http(Token(A2ARoles.Partner)).PostAsync("/mcp/customer",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SubjectToken_CannotUseThePartnerCatalogEndpoint()
    {
        var response = await Http(Token(A2ARoles.Subject, "1027")).PostAsync("/mcp/catalog",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdinaryUserToken_IsRejectedOnBothEndpoints()
    {
        foreach (var role in new[] { "Admin", "Agent", "Customer" })
        foreach (var path in new[] { "/mcp/catalog", "/mcp/customer" })
        {
            var response = await Http(Token(role, role == "Customer" ? "1027" : null)).PostAsync(path,
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
            response.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], $"{role} → {path}");
        }
    }

    // ═══ tool kümeleri ═══

    [Fact]
    public async Task CatalogEndpoint_ListsOnlyProductTools_AndTheyWork()
    {
        await using var client = await ConnectAsync("/mcp/catalog", Token(A2ARoles.Partner));

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        tools.Select(t => t.Name).Should().BeEquivalentTo([WellKnown.ToolNames.ProductInquiry, WellKnown.ToolNames.ProductList]);

        var result = await client.CallToolAsync(WellKnown.ToolNames.ProductList, new Dictionary<string, object?>(), cancellationToken: Ct);
        result.IsError.Should().NotBe(true);
        TextOf(result).Should().Contain("Kategori A");
    }

    [Fact]
    public async Task CustomerEndpoint_ListsOnlyReadOnlyTools_NoneWithSideEffects()
    {
        await using var client = await ConnectAsync("/mcp/customer", Token(A2ARoles.Subject, "1027"));

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).Should().Contain(WellKnown.ToolNames.GetLastOrder);
        tools.Select(t => t.Name).Should().NotContain(n => WellKnown.SideEffectToolOwners.ContainsKey(n));
        tools.Select(t => t.Name).Should().NotContain(WellKnown.ToolNames.ProductList);
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations != null && t.ProtocolTool.Annotations.ReadOnlyHint == true,
            "istemciler salt-okunur olduğunu araç açıklamasından görebilmeli");
    }

    // ═══ müşteri izolasyonu ═══

    [Fact]
    public async Task CustomerTools_UseTheCustomerFromTheToken_NeverFromTheArguments()
    {
        await using var client = await ConnectAsync("/mcp/customer", Token(A2ARoles.Subject, "1027"));

        var result = await client.CallToolAsync(WellKnown.ToolNames.GetLastOrder, new Dictionary<string, object?>(), cancellationToken: Ct);

        TextOf(result).Should().Contain("1027");
        _factory.LastOrderCustomerIds.Should().Contain("1027");
    }

    [Fact]
    public async Task ConcurrentCallsFromDifferentCustomers_DoNotSeeEachOthersIdentity()
    {
        // Stateless mod + istek başına ambient kapsam: yarış koşulunda kimlik karışmamalı.
        async Task<string> CallAs(string customerId)
        {
            await using var client = await ConnectAsync("/mcp/customer", Token(A2ARoles.Subject, customerId));
            var results = new List<string>();
            for (var i = 0; i < 5; i++)
                results.Add(TextOf(await client.CallToolAsync(WellKnown.ToolNames.GetLastOrder, new Dictionary<string, object?>(), cancellationToken: Ct)));
            return string.Join("|", results);
        }

        var both = await Task.WhenAll(CallAs("1027"), CallAs("2002"));

        both[0].Should().NotContain("2002");
        both[1].Should().NotContain("1027");
        both[0].Should().Contain("1027");
        both[1].Should().Contain("2002");
    }

    [Fact]
    public async Task SubjectTokenWithoutLinkedCustomer_IsRefused()
    {
        var response = await Http(Token(A2ARoles.Subject, customerId: " ")).PostAsync("/mcp/customer",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

public class McpDisabledByDefaultTests : IClassFixture<McpDisabledFactory>
{
    private readonly McpDisabledFactory _factory;
    public McpDisabledByDefaultTests(McpDisabledFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/mcp/catalog")]
    [InlineData("/mcp/customer")]
    public async Task WhenDisabled_TheEndpointsDoNotExist(string path)
    {
        var response = await _factory.CreateClient().PostAsync(path,
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "kapalı kanal yayında olmamalı (yetkiyle engellenmesi değil)");
    }
}
