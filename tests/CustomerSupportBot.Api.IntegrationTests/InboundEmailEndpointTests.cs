// Gelen e-posta webhook'u: kapalıyken görünmez, gizli doğrulanmadan hiçbir şey işlenmez, yanıt gövdesi hiçbir şey sızdırmaz.

using System.Net;
using System.Net.Http.Json;
using CustomerSupportBot.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;

namespace CustomerSupportBot.Api.IntegrationTests;

public class InboundEmailEndpointTests
{
    private const string Secret = "inbound-test-secret-0123456789";

    private sealed class EnabledFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("InboundEmail:Enabled", "true");
            builder.UseSetting("InboundEmail:Secret", Secret);
        }
    }

    private sealed class EnabledWithoutSecretFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("InboundEmail:Enabled", "true");
        }
    }

    private static object Body(string from = "kimse@example.com") =>
        new { from, subject = "x", text = "merhaba", messageId = Guid.NewGuid().ToString("N"), authenticated = true };

    private static Task<HttpResponseMessage> PostAsync(HttpClient c, object body, string? secret)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/channels/email/inbound") { Content = JsonContent.Create(body) };
        if (secret is not null) req.Headers.Add(InboundEmailEndpoints.SecretHeader, secret);
        return c.SendAsync(req, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenDisabled_TheEndpointDoesNotExist()
    {
        await using var factory = new TestWebApplicationFactory();

        (await PostAsync(factory.CreateClient(), Body(), Secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("yanlis")]
    public async Task WithoutTheRightSecret_NothingIsProcessed(string? secret)
    {
        await using var factory = new EnabledFactory();

        (await PostAsync(factory.CreateClient(), Body(), secret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnabledWithoutAConfiguredSecret_RejectsEverything()
    {
        await using var factory = new EnabledWithoutSecretFactory();

        (await PostAsync(factory.CreateClient(), Body(), "")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(factory.CreateClient(), Body(), "herhangi")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownSender_GetsTheSameEmpty202_AsAKnownOne()
    {
        await using var factory = new EnabledFactory();

        var r = await PostAsync(factory.CreateClient(), Body("kimse-yok@example.com"), Secret);

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().BeEmpty("sonuç gövdede sızdırılmaz");
    }

    [Fact]
    public async Task AMissingFrom_IsABadRequest()
    {
        await using var factory = new EnabledFactory();

        (await PostAsync(factory.CreateClient(), new { subject = "x", text = "y", authenticated = true }, Secret))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
