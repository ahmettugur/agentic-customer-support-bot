// Fotoğraf ekleme uçları: yükleme (oturum açma dahil), tür/boyut reddi, oturum sahipliği,
// müşterinin yalnızca kendi fotoğrafını alması, personelin onay kartı için erişimi.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ChatAttachmentEndpointTests : IClassFixture<ChatAttachmentEndpointTests.Factory>
{
    /// <summary>
    /// Görsel model yerine sabit açıklama — testler ağa çıkmaz. Store bellek içi ikiziyle değişir:
    /// Postgres store'un koşullu UPDATE/DELETE'i EF InMemory sağlayıcısında yok; o store
    /// <c>PostgresAttachmentStoreTests</c>'te gerçek Postgres'le sınanıyor.
    /// </summary>
    public sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var analyzer = Substitute.For<IImageAnalysisPort>();
                analyzer.DescribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns("Kulpu kırık beyaz kupa.");
                services.RemoveAll<IImageAnalysisPort>();
                services.AddSingleton(analyzer);
                services.RemoveAll<IAttachmentStore>();
                services.AddSingleton<IAttachmentStore, InMemoryAttachmentStore>();
            });
        }
    }

    private readonly Factory _factory;
    public ChatAttachmentEndpointTests(Factory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>EXIF (içinde "Leak" metni) taşıyan en küçük geçerli JPEG.</summary>
    private static byte[] Jpeg()
    {
        var exif = "Exif\0\0II*\0\x08\0\0\0\x01\0\x0F\x01\x02\0\x05\0\0\0\x1A\0\0\0\0\0\0\0Leak\0"u8.ToArray();
        byte[] app1 = [0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif];
        return [0xFF, 0xD8, .. app1, 0xFF, 0xDB, 0, 4, 0, 1, 0xFF, 0xDA, 0, 3, 1, 0x11, 0x22, 0xFF, 0xD9];
    }

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

    private static MultipartFormDataContent Form(byte[] data, string? sessionId = null, string fileName = "foto.jpg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", fileName);
        if (sessionId is not null) form.Add(new StringContent(sessionId), "sessionId");
        return form;
    }

    private static async Task<JsonElement> UploadOkAsync(HttpClient client, string? sessionId = null)
    {
        var resp = await client.PostAsync("/chat/attachments", Form(Jpeg(), sessionId), Ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync(Ct));
        return await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Upload_WithoutSession_CreatesOne_AndReturnsTheAnalysis()
    {
        var customer = ClientFor("Customer", "1001");

        var body = await UploadOkAsync(customer);

        body.GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("description").GetString().Should().Be("Kulpu kırık beyaz kupa.");

        var id = body.GetProperty("attachmentId").GetString();
        var image = await customer.GetAsync($"/chat/attachments/{id}", Ct);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        image.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        var bytes = await image.Content.ReadAsByteArrayAsync(Ct);
        System.Text.Encoding.ASCII.GetString(bytes).Should().NotContain("Leak", "EXIF meta verisi silinmiş olmalı");
    }

    [Fact]
    public async Task Upload_IntoAnotherCustomersSession_Is403()
    {
        var owner = await UploadOkAsync(ClientFor("Customer", "1001"));
        var intruder = ClientFor("Customer", "2002");

        var resp = await intruder.PostAsync("/chat/attachments",
            Form(Jpeg(), owner.GetProperty("sessionId").GetString()), Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_NonImage_WithJpgName_Is400()
    {
        var resp = await ClientFor("Customer", "1001").PostAsync("/chat/attachments",
            Form("<svg onload=alert(1)>"u8.ToArray()), Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("unsupported_type");
    }

    [Fact]
    public async Task Upload_TooLarge_Is400()
    {
        var big = new byte[6 * 1024 * 1024];
        Jpeg().CopyTo(big, 0);

        var resp = await ClientFor("Customer", "1001").PostAsync("/chat/attachments", Form(big), Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("too_large");
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersPhoto()
    {
        var owner = await UploadOkAsync(ClientFor("Customer", "1001"));

        var resp = await ClientFor("Customer", "2002")
            .GetAsync($"/chat/attachments/{owner.GetProperty("attachmentId").GetString()}", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_CanReadPhotos_CustomerCannotUseStaffRoute()
    {
        var id = (await UploadOkAsync(ClientFor("Customer", "1001"))).GetProperty("attachmentId").GetString();

        (await ClientFor("Admin").GetAsync($"/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ClientFor("Agent").GetAsync($"/agent/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ClientFor("Customer", "1001").GetAsync($"/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync($"/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_Anonymous_Is401()
    {
        var resp = await _factory.CreateClient().PostAsync("/chat/attachments", Form(Jpeg()), Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_OwnUnsentPhoto_Is204_OthersAre404()
    {
        var owner = ClientFor("Customer", "1001");
        var id = (await UploadOkAsync(owner)).GetProperty("attachmentId").GetString();

        (await ClientFor("Customer", "2002").DeleteAsync($"/chat/attachments/{id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/chat/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/chat/attachments/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
