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

    // ── Sağlayıcı uçları ───────────────────────────────────────────────────────

    private static string BasicHeader(string password) => "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("inbound:" + password));

    private static HttpRequestMessage Postmark(string json, string? basic) 
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/channels/email/inbound/postmark")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        if (basic is not null) req.Headers.TryAddWithoutValidation("Authorization", basic);
        return req;
    }

    private const string PostmarkJson = """{"FromFull":{"Email":"kimse-yok@example.com"},"TextBody":"merhaba","MessageID":"pm-1","Headers":[]}""";

    [Fact]
    public async Task Postmark_AcceptsTheSecretAsAHttpBasicPassword()
    {
        await using var factory = new EnabledFactory();

        var r = await factory.CreateClient().SendAsync(Postmark(PostmarkJson, BasicHeader(Secret)), TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic eWFubGlzOnlhbmxpcw==")]
    public async Task Postmark_WithoutTheRightSecret_IsUnauthorized(string? basic)
    {
        await using var factory = new EnabledFactory();

        var r = await factory.CreateClient().SendAsync(Postmark(PostmarkJson, basic), TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Postmark_AMalformedPayload_IsABadRequest()
    {
        await using var factory = new EnabledFactory();

        var r = await factory.CreateClient().SendAsync(Postmark("""{"TextBody":"x"}""", BasicHeader(Secret)), TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendGrid_AcceptsAFormPost_AndRejectsABadSecret()
    {
        await using var factory = new EnabledFactory();
        var client = factory.CreateClient();

        HttpRequestMessage Req(string? basic)
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent("Kimse <kimse-yok@example.com>"), "from" },
                { new StringContent("merhaba"), "text" },
                { new StringContent("{@example.com : pass}"), "dkim" }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "/channels/email/inbound/sendgrid") { Content = form };
            if (basic is not null) req.Headers.TryAddWithoutValidation("Authorization", basic);
            return req;
        }

        (await client.SendAsync(Req(BasicHeader(Secret)), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(Req(BasicHeader("yanlis")), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendGrid_ANonFormBody_IsABadRequest()
    {
        await using var factory = new EnabledFactory();
        var req = new HttpRequestMessage(HttpMethod.Post, "/channels/email/inbound/sendgrid") { Content = JsonContent.Create(new { a = 1 }) };
        req.Headers.TryAddWithoutValidation("Authorization", BasicHeader(Secret));

        (await factory.CreateClient().SendAsync(req, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
