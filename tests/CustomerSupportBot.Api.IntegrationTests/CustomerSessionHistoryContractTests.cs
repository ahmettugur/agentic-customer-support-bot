// Sohbet ekranının geçmiş konuşma istemcisi ile GERÇEK sunucu arasındaki sözleşme.
//
// Bu iki istemci yöntemi (GetSessionsAsync / GetSessionMessagesAsync) yazıldığında hiçbir yerden çağrılmıyordu ve
// sunucunun döndürdüğü şekille (role/text) uyuşmuyordu — sessizce boş liste dönerdi. Sözleşmeyi gerçek uçla ölçer.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class CustomerSessionHistoryContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public CustomerSessionHistoryContractTests(TestWebApplicationFactory f) => _factory = f;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Password = "Pass#1234";

    private async Task<HttpClient> LoginAsync(string username, string linkedCustomerId)
    {
        var dbf = _factory.GetDbContextFactory();
        await using (var ctx = await dbf.CreateDbContextAsync(Ct))
        {
            ctx.Users.Add(new UserEntity
            {
                Id = Guid.NewGuid().ToString("N"), Username = username, PasswordHash = new BCryptPasswordHasher().Hash(Password),
                Role = "Customer", LinkedCustomerId = linkedCustomerId, IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync(Ct);
        }
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new { Username = username, Password }, Ct);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private async Task<string> SeedConversationAsync(string customerId, string userText, string botText)
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();
        var session = await sessions.GetOrCreateAsync(null, CancellationToken.None);
        session.State.AuthenticatedCustomerId = customerId;
        await sessions.UpdateAsync(session, CancellationToken.None);
        await sessions.AddExchangeAsync(session.SessionId, userText, botText, ct: CancellationToken.None);
        return session.SessionId;
    }

    [Fact]
    public async Task ClientReadsTheCustomersOwnSessionsAndMessages_WithTheRealServerShape()
    {
        var sessionId = await SeedConversationAsync("2101", "Siparişim nerede?", "Siparişiniz kargoda.");
        var api = new ChatApiService(await LoginAsync("hist-owner", "2101"));

        var sessions = await api.GetSessionsAsync();
        var messages = await api.GetSessionMessagesAsync(sessionId);

        var mine = sessions.Single(s => s.SessionId == sessionId);
        mine.Title.Should().Be("Siparişim nerede?");
        mine.MessageCount.Should().Be(2);
        messages.Select(m => (m.Role, m.Text)).Should().Equal(("user", "Siparişim nerede?"), ("bot", "Siparişiniz kargoda."));
    }

    [Fact]
    public async Task ClientNeverSeesAnotherCustomersSessions()
    {
        var foreign = await SeedConversationAsync("2102", "gizli soru", "gizli yanıt");
        var api = new ChatApiService(await LoginAsync("hist-intruder", "2103"));

        (await api.GetSessionsAsync()).Should().NotContain(s => s.SessionId == foreign);
        (await api.GetSessionMessagesAsync(foreign)).Should().BeEmpty("sahiplik kontrolü 403 döner; istemci boş liste verir");
    }
}
