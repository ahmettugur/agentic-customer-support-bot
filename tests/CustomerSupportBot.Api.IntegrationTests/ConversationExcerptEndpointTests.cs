// Onay ve eskalasyon kartlarının konuşma özeti müşteri mesajı içerir: yalnızca kaydı görebilen personele açık olmalı.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ConversationExcerptEndpointTests
{
    private static readonly DateTime At = new(2026, 11, 1, 18, 51, 58, DateTimeKind.Utc);

    private static (WebApplicationFactory<global::Program> Factory, TestWebApplicationFactory Root) Build()
    {
        var queue = Substitute.For<IApprovalQueue>();
        queue.GetAsync("a1", Arg.Any<CancellationToken>()).Returns(new ApprovalRequest
        {
            Id = "a1", SessionId = "s1", UserQuery = "Evet, gönderebilirsiniz", RequestedAt = At,
            ToolName = WellKnown.ToolNames.OrderPlacement
        });
        queue.GetAsync("yok", Arg.Any<CancellationToken>()).Returns((ApprovalRequest?)null);
        var excerpts = Substitute.For<IConversationExcerptStore>();
        excerpts.GetBeforeAsync("s1", At, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new ConversationMessage(ConversationRoles.User, "3 adet"),
            new ConversationMessage(ConversationRoles.Assistant, "Onaya göndermemi ister misiniz?")
        ]);

        var root = new TestWebApplicationFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IApprovalQueue>();
            s.AddSingleton(queue);
            s.RemoveAll<IConversationExcerptStore>();
            s.AddSingleton(excerpts);
        }));
        return (factory, root);
    }

    private static HttpClient ClientFor(WebApplicationFactory<global::Program> factory, string role)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo("u1", "u1", "", role, "u1", true, DateTime.UtcNow, null), DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData("Admin", "/approvals/a1/conversation")]
    [InlineData("Agent", "/agent/approvals/a1/conversation")]
    public async Task Staff_SeeTheConversationThatLedToTheRequest(string role, string path)
    {
        var (factory, root) = Build();
        using var _ = root;
        using var f = factory;

        var lines = await ClientFor(f, role).GetFromJsonAsync<JsonElement>(path, TestContext.Current.CancellationToken);

        lines.EnumerateArray().Select(l => l.GetProperty("text").GetString())
            .Should().Equal("3 adet", "Onaya göndermemi ister misiniz?", "Evet, gönderebilirsiniz");
        lines[2].GetProperty("speaker").GetString().Should().Be("customer");
    }

    [Fact]
    public async Task UnknownApproval_Is404()
    {
        var (factory, root) = Build();
        using var _ = root;
        using var f = factory;

        var response = await ClientFor(f, "Admin").GetAsync("/approvals/yok/conversation", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Customer", "/approvals/a1/conversation")]
    [InlineData("Customer", "/agent/approvals/a1/conversation")]
    [InlineData("Agent", "/approvals/a1/conversation")]
    public async Task OthersCannotReadIt(string role, string path)
    {
        var (factory, root) = Build();
        using var _ = root;
        using var f = factory;

        var response = await ClientFor(f, role).GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var (factory, root) = Build();
        using var _ = root;
        using var f = factory;

        var response = await f.CreateClient().GetAsync("/approvals/a1/conversation", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static WebApplicationFactory<global::Program> BuildEscalations(TestWebApplicationFactory root)
    {
        var sink = Substitute.For<IEscalationSink>();
        sink.GetOpen().Returns(
        [
            new EscalationRequest { Id = "serbest", SessionId = "s1", UserQuery = "Temsilci istiyorum", CreatedAt = At },
            new EscalationRequest { Id = "bana", SessionId = "s2", UserQuery = "Beni arayın", CreatedAt = At, AssignedTo = "u1" },
            new EscalationRequest { Id = "baskasina", SessionId = "s3", UserQuery = "Gizli konu", CreatedAt = At, AssignedTo = "baska" }
        ]);
        var excerpts = Substitute.For<IConversationExcerptStore>();
        excerpts.GetBeforeAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConversationMessage>());
        return root.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IEscalationSink>();
            s.AddSingleton(sink);
            s.RemoveAll<IConversationExcerptStore>();
            s.AddSingleton(excerpts);
        }));
    }

    [Fact]
    public async Task Admin_GetsEveryOpenEscalationInOneRequest()
    {
        using var root = new TestWebApplicationFactory();
        using var f = BuildEscalations(root);

        var map = await ClientFor(f, "Admin").GetFromJsonAsync<Dictionary<string, JsonElement>>(
            "/escalations/open/conversations", TestContext.Current.CancellationToken);

        map!.Keys.Should().BeEquivalentTo("serbest", "bana", "baskasina");
        map["bana"][0].GetProperty("text").GetString().Should().Be("Beni arayın");
    }

    [Fact]
    public async Task Agent_GetsOnlyWhatTheOpenListShowsThem()
    {
        // Açık liste kapsamının dolaşılmaması gerekir: başka temsilcinin kaydındaki müşteri mesajları burada da görünmemeli.
        using var root = new TestWebApplicationFactory();
        using var f = BuildEscalations(root);

        var map = await ClientFor(f, "Agent").GetFromJsonAsync<Dictionary<string, JsonElement>>(
            "/agent/escalations/open/conversations", TestContext.Current.CancellationToken);

        map!.Keys.Should().BeEquivalentTo("serbest", "bana");
    }

    [Theory]
    [InlineData("Customer", "/escalations/open/conversations")]
    [InlineData("Customer", "/agent/escalations/open/conversations")]
    [InlineData("Agent", "/escalations/open/conversations")]
    public async Task OthersCannotReadEscalationConversations(string role, string path)
    {
        using var root = new TestWebApplicationFactory();
        using var f = BuildEscalations(root);

        var response = await ClientFor(f, role).GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
