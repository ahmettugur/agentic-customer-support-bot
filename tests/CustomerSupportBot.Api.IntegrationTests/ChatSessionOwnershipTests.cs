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
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

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

    // ─── /sessions/* — chat uçlarıyla aynı kural geçerli olmalı ─────────────

    [Fact]
    public async Task SessionMessages_ForeignSession_Returns403()
    {
        // Konuşmanın tamamı buradan okunabilir; /chat/events ile aynı korumayı gerektirir.
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-5", "9995");

        var resp = await intruder.GetAsync($"/sessions/{sessionId}/messages",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SessionState_ForeignSession_Returns403()
    {
        var sessionId = await SeedSessionOwnedByAsync("1027");
        var intruder = await LoginAsync("own-intruder-6", "9994");

        var resp = await intruder.GetAsync($"/sessions/{sessionId}/state",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SessionMessages_OwnSession_Returns200()
    {
        var sessionId = await SeedSessionOwnedByAsync("1031");
        var owner = await LoginAsync("own-owner-2", "1031");

        var resp = await owner.GetAsync($"/sessions/{sessionId}/messages",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SessionList_OnlyContainsTheCallersOwnSessions()
    {
        // Liste ucunda "sahiplik" karşılığı budur. Filtrelenmezse müşteri, başkalarının oturum
        // kimliklerini öğrenir — tek tek uçlar korunsa bile bu bilgi tek başına sızıntıdır.
        var mine = await SeedSessionOwnedByAsync("1032");
        var someoneElses = await SeedSessionOwnedByAsync("1033");

        var client = await LoginAsync("own-lister", "1032");
        var list = await client.GetFromJsonAsync<List<SessionListRow>>(
            "/sessions/", TestContext.Current.CancellationToken);

        list.Should().NotBeNull();
        list!.Should().Contain(r => r.SessionId == mine);
        list.Should().NotContain(r => r.SessionId == someoneElses,
            "başka bir müşterinin oturum kimliği listede görünmemeli");
    }
    // ─── A2A token'ları bu uçlarda geçerli OLMAMALI ─────────────────────────

    /// <summary>Belirtilen rolde ham bir JWT üretir — login akışından geçmeden.</summary>
    private HttpClient TokenClient(string role, string? linkedCustomerId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: Guid.NewGuid().ToString("N"),
            Username: $"a2a:{role}",
            PasswordHash: "", Role: role, LinkedAgentId: null, IsActive: true,
            CreatedAt: DateTime.UtcNow, LastLoginAt: null, LinkedCustomerId: linkedCustomerId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    [Fact]
    public async Task SessionList_WithPartnerToken_IsForbidden()
    {
        // Partner token'ında linked_customer_id claim'i YOKTUR. Kapsam "claim yoksa sınırsız"
        // kuralıyla hesaplansaydı partner admin gibi değerlendirilip TÜM müşterilerin
        // oturumlarını listeleyebilirdi. A2A token'ları yalnızca /a2a uçlarında geçerlidir.
        await SeedSessionOwnedByAsync("1041");
        var partner = TokenClient(A2ARoles.Partner);

        var resp = await partner.GetAsync("/sessions/", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SessionMessages_WithA2ASubjectToken_IsForbidden()
    {
        // Özne token'ı bir müşteriye BAĞLIDIR, dolayısıyla kapsam kontrolünü geçerdi; ama
        // yetki alanı yalnızca A2A ajan çağrılarıdır — müşterinin normal sohbet geçmişi değil.
        var sessionId = await SeedSessionOwnedByAsync("1042");
        var subject = TokenClient(A2ARoles.Subject, linkedCustomerId: "1042");

        var resp = await subject.GetAsync($"/sessions/{sessionId}/messages",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Onay bildirimleri müşteriye de bağlı olmalı ────────────────────────

    [Fact]
    public async Task UnseenApprovals_ForADeletedSession_AreNotReadableByAnotherCustomer()
    {
        // Onay kayıtları oturumdan bağımsız yaşar (session_id için yabancı anahtar yok) ve
        // oturum sahipliği kontrolü VAR OLMAYAN oturumlara izin verir — ilk temas onu çağırana
        // bağlasın diye. Bu ikisi birleşince, sessionId'yi öğrenen başka bir müşteri silinmiş
        // bir oturumun bildirimlerini okuyabilirdi. Sorgu müşteri kimliğiyle de daraltılmalı.
        var orphanSessionId = $"gone-{Guid.NewGuid():N}";

        // Kayıt doğrudan yazılıyor, DecideAsync üzerinden DEĞİL: bu test altyapısı EF InMemory
        // sağlayıcısını kullanıyor ve karar yolundaki ExecuteUpdateAsync'i desteklemiyor
        // (üretimdeki Npgsql destekler). Testin hedefi zaten karar mantığı değil, unseen
        // sorgusunun müşteriye göre daralıp daralmadığı.
        var dbf = _factory.GetDbContextFactory();
        await using (var ctx = await dbf.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            ctx.Approvals.Add(new ApprovalRequestEntity
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                SessionId = orphanSessionId,
                CustomerId = "1043",              // sahibi
                ToolName = WellKnown.ToolNames.OrderCancel,
                ParametersJson = "{}",
                RequestedAt = DateTime.UtcNow,
                DecidedAt = DateTime.UtcNow,
                Status = nameof(ApprovalStatus.Rejected),
                ExecutionStatus = nameof(ApprovalExecutionStatus.None),
                TimeoutSeconds = 60
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var intruder = await LoginAsync("own-intruder-7", "9993");

        var resp = await intruder.GetAsync(
            $"/chat-sessions/{orphanSessionId}/approvals/unseen", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "oturum yok, sahiplik kontrolü geçer");
        var rows = await resp.Content.ReadFromJsonAsync<List<UnseenRow>>(
            cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().BeEmpty("bildirim başka bir müşteriye ait — listelenmemeli");
    }
    // ─── Agent kapsamı: eksik claim, Admin kapsamına yükselmemeli ──────────

    [Fact]
    public async Task AgentEscalations_WithoutLinkedAgentClaim_IsNotTreatedAsAdmin()
    {
        // LinkedAgentId veritabanında nullable'dır, yani bu hesap gerçekten oluşabilir.
        // "claim yoksa hepsini göster" kuralı böyle bir Agent'ı sessizce Admin kapsamına
        // yükseltirdi — üstelik eylem uçları (acknowledge/resolve) claim yoksa zaten 400 döner,
        // yani liste uçları onlardan daha genişti.
        var agent = TokenClient("Agent");   // linked_agent_id YOK

        foreach (var path in new[] { "/agent/escalations/open", "/agent/escalations/recent" })
        {
            var resp = await agent.GetAsync(path, TestContext.Current.CancellationToken);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"{path} bağlantısı olmayan agent'a tüm kayıtları vermemeli");
        }
    }

    [Fact]
    public async Task AdminEscalations_AreNotScoped()
    {
        // Admin için sınırsız kapsam DOĞRU davranıştır; düzeltmenin onu bozmadığını sabitler.
        var admin = TokenClient("Admin");

        var resp = await admin.GetAsync("/agent/escalations/open", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record UnseenRow(string Id);
    private sealed record SessionListRow(string SessionId);
}
