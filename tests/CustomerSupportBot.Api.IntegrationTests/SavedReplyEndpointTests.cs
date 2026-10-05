// Hazır yanıt uçları: yönetici yönetir, temsilci okur; durum kodları.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public class SavedReplyEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..20], "", role,
                role == "Agent" ? "agent-1" : null, true, DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_Manages_AgentReads()
    {
        var admin = ClientFor("Admin");
        var shortcut = $"kapanis-{Guid.NewGuid():N}"[..20];

        var created = await admin.PostAsJsonAsync("/saved-replies",
            new { title = "Kapanış", body = "İyi günler dileriz.", shortcut }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();

        var list = await ClientFor("Agent").GetFromJsonAsync<JsonElement>($"/agent/saved-replies?q={shortcut}", Ct);
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("body").GetString().Should().Be("İyi günler dileriz.");

        (await admin.PutAsJsonAsync($"/saved-replies/{id}", new { title = "Kapanış", body = "Görüşmek üzere.", shortcut }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/saved-replies/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/saved-replies/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validation_And_Conflict_StatusCodes()
    {
        var admin = ClientFor("Admin");
        var shortcut = $"iade-{Guid.NewGuid():N}"[..20];
        await admin.PostAsJsonAsync("/saved-replies", new { title = "İade", body = "14 gün.", shortcut }, Ct);

        (await admin.PostAsJsonAsync("/saved-replies", new { title = "", body = "x" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/saved-replies", new { title = "İade 2", body = "x", shortcut = shortcut.ToUpperInvariant() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync("/saved-replies/yok", new { title = "a", body = "b" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AgentsCannotWrite_CustomersCannotRead()
    {
        (await ClientFor("Agent").PostAsJsonAsync("/saved-replies", new { title = "x", body = "y" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer").GetAsync("/agent/saved-replies", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/saved-replies", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
