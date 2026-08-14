// Oturum sahipliğinin HTTP sınırında uygulandığını doğrular.
//
// Bu uçlar "Customer" politikasıyla korunuyordu ama sessionId istemciden geldiği için
// kimlik doğrulama tek başına yetmiyordu: geçerli bir müşteri token'ıyla BAŞKA bir
// müşterinin sessionId'si verilerek onun canlı olay akışı ve onay bildirimleri
// okunabiliyordu.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.Tests.Endpoints;

public class ChatSessionOwnershipTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public ChatSessionOwnershipTests(TestWebApplicationFactory f) => _factory = f;

    private const string Password = "Pass#1234";

    /// <summary>Login olabilen, belirli bir müşteriye bağlı bir hesap oluşturur.</summary>
    private async Task SeedCustomerAsync(string username, string linkedCustomerId)
    {
        var dbf = _factory.GetDbContextFactory();
        await using var ctx = await dbf.CreateDbContextAsync();
        if (ctx.Users.Any(u => u.Username == username)) return;

        ctx.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = new BCryptPasswordHasher().Hash(Password),
            Role = "Customer",
            LinkedCustomerId = linkedCustomerId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsync(string username, string linkedCustomerId)
    {
        await SeedCustomerAsync(username, linkedCustomerId);
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/auth/login",
            new { Username = username, Password },
            cancellationToken: TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    /// <summary>Belirli bir müşteriye bağlanmış bir oturumu doğrudan store'a yazar.</summary>
    private async Task<string> SeedSessionOwnedByAsync(string ownerCustomerId)
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();

        var session = await sessions.GetOrCreateAsync(null, CancellationToken.None);
        session.State.AuthenticatedCustomerId = ownerCustomerId;
        await sessions.UpdateAsync(session, CancellationToken.None);
        return session.SessionId;
    }

    [Fact]
    public async Task UnseenApprovals_ForeignSession_Returns403()
    {
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-1", "9999");

        var resp = await intruder.GetAsync($"/chat-sessions/{sessionId}/approvals/unseen",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnseenApprovals_OwnSession_Returns200()
    {
        var sessionId = await SeedSessionOwnedByAsync("1028");
        var owner = await LoginAsync("own-owner-1", "1028");

        var resp = await owner.GetAsync($"/chat-sessions/{sessionId}/approvals/unseen",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChatEvents_ForeignSession_Returns403()
    {
        // Bu uç oturumun TÜM canlı olaylarını yayınlar — bot yanıtları, onay sonuçları,
        // temsilci mesajları. Sahiplik kontrolü olmadan başkasının konuşması dinlenebilirdi.
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-2", "9998");

        var resp = await intruder.GetAsync($"/chat/events/{sessionId}",
            HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarkApprovalSeen_ForeignSession_Returns403()
    {
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-3", "9997");

        var resp = await intruder.PostAsync(
            $"/chat-sessions/{sessionId}/approvals/abc123/seen", null,
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Chat_ForeignSession_Returns403()
    {
        // 403 LLM'e hiç gitmeden dönmeli — tur başlamadan reddedilir.
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-4", "9996");

        var resp = await intruder.PostAsJsonAsync("/chat/",
            new { Query = "siparişim nerede", SessionId = sessionId },
            cancellationToken: TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnseenApprovals_UnknownSession_IsNotForbidden()
    {
        // Var olmayan oturum yasak değildir — ilk temas onu çağırana bağlar.
        var client = await LoginAsync("own-newcomer", "1029");

        var resp = await client.GetAsync("/chat-sessions/henuz-yok/approvals/unseen",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
