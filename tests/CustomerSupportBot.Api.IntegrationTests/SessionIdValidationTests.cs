// İstemcinin gönderdiği sessionId'nin BİÇİMİ.
//
// sessionId istemciden gelir (gövde veya URL) ve doğrudan chat.sessions.session_id
// (varchar(64)) anahtarı olur. Doğrulama yoktu: 64 karakterden uzun bir id önce süreç içi
// cache'e ekleniyor, ardından DB yazması patlıyordu — tur ham bir DB hatasıyla bitiyor, cache'te
// ise hiç kaydedilmemiş bir oturum kalıyordu. Keyfi karakterler de (boşluk, yol ayraçları,
// HTML) log ve panel yüzeylerine taşınıyordu. Kural oturum OLUŞTURAN/BAĞLAYAN uçlarda,
// oturuma dokunmadan önce uygulanır.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class SessionIdValidationTests
{
    private static readonly string TooLong = new('a', 65);

    /// <summary>Gerçek OpenAI Realtime'a bağlanmadan dönen sesli kanal — yalnızca el sıkışma ölçülür.</summary>
    private sealed class NoopRealtimeBridge : IRealtimeNativeBridge
    {
        public Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRealtimeNativeBridge, NoopRealtimeBridge>());
            });
        }
    }

    private static string CustomerToken(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: "user-1001", Username: "1001@example.com", PasswordHash: "", Role: "Customer",
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: "1001");
        return provider.GenerateAccessToken(user, DateTime.UtcNow).Token;
    }

    private static HttpClient CustomerClient(Factory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken(factory));
        return client;
    }

    /// <summary>Boş sorgu: geçerli bir id ile InputGuard'da durur, LLM'e hiç gitmez.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> ChatAsync(HttpClient client, string sessionId, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/chat/", new { query = "", sessionId }, ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chat_RejectsAnInvalidSessionId(bool overlong)
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();

        var (status, body) = await ChatAsync(CustomerClient(factory), overlong ? TooLong : "../oturum 1", ct);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("invalid_session_id");
    }

    [Fact]
    public async Task Chat_AcceptsAServerIssuedSessionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();

        var (_, body) = await ChatAsync(CustomerClient(factory), Guid.NewGuid().ToString(), ct);

        body.Should().NotContain("invalid_session_id", "sunucunun ürettiği biçim her zaman geçerli olmalı");
    }

    [Fact]
    public async Task ChatStream_RejectsAnInvalidSessionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();

        var response = await CustomerClient(factory).PostAsJsonAsync("/chat/stream", new { query = "", sessionId = TooLong }, ct);

        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("invalid_session_id");
    }

    [Fact]
    public async Task ChatEvents_RejectsAnInvalidSessionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();

        using var response = await CustomerClient(factory).GetAsync(
            $"/chat/events/{TooLong}", HttpCompletionOption.ResponseHeadersRead, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Realtime_RejectsAnInvalidSessionIdBeforeAcceptingTheSocket()
    {
        const string path = "/chat/realtime-native/";
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();
        var client = factory.Server.CreateWebSocketClient();
        var token = CustomerToken(factory);
        client.ConfigureRequest = r => r.Headers.Authorization = $"Bearer {token}";

        var connect = () => client.ConnectAsync(new Uri(factory.Server.BaseAddress, path + TooLong), ct);

        await connect.Should().ThrowAsync<Exception>("el sıkışma 400 ile reddedilmeli, soket hiç açılmamalı");
    }
}
