// A2A session izolasyonunun Microsoft wrapper'ını değil, uygulamanın ÜRETİM DI BAĞLANTISINI
// doğrular. Test yalnızca ham backing store kaydeder; provider ve isolation decorator
// AddA2AAgents() içindeki UseClaimsBasedAgentIsolation() ile gerçek A2AServer factory'sinden gelir.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Adapters.Agents.A2A;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public sealed class A2AIsolationFactory : A2AConformanceFactory
{
    public RecordingAgentSessionStore SessionStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Yalnızca çıplak store'u veriyoruz. IsolationKeyScopedAgentSessionStore veya
        // AgentIsolationKeyProvider burada kaydedilirse test üretim bağlantısını atlar ve
        // UseClaimsBasedAgentIsolation() silindiğinde yanlışlıkla yeşil kalır.
        //
        // MAAI001 yalnızca BURADA bastırılıyor: AgentSessionStore/AgentSessionStoreKey henüz
        // deneysel işaretli (bkz. Api/Extensions/A2AServicesExtensions.cs'teki MEAI001 notu —
        // aynı gerekçe).
#pragma warning disable MAAI001
        builder.ConfigureServices(services =>
            services.AddKeyedSingleton<AgentSessionStore>(
                A2AAgentNames.Order,
                SessionStore));
#pragma warning restore MAAI001
    }
}

#pragma warning disable MAAI001
public sealed class RecordingAgentSessionStore : AgentSessionStore
{
    // 1.22 SDK'sında AgentSessionStore artık düz bir string yerine AgentSessionStoreKey alıyor:
    // SessionId (wire contextId) ile Partitions (izolasyon anahtarı dahil, sözlük) ayrı taşınıyor.
    // IsolationKeyScopedAgentSessionStore izolasyon değerini "isolation" adlı partition'a yazıyor —
    // artık eski string-birleştirme + kaçış (escape) yok, ham NameIdentifier claim değeri saklanıyor.
    private readonly ConcurrentQueue<AgentSessionStoreKey> _observedKeys = new();

    public IReadOnlyCollection<AgentSessionStoreKey> ObservedKeys => _observedKeys.ToArray();

    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        _observedKeys.Enqueue(key);
        return await agent.CreateSessionAsync(cancellationToken);
    }

    public override ValueTask SaveSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        _observedKeys.Enqueue(key);
        return ValueTask.CompletedTask;
    }
}
#pragma warning restore MAAI001

public class A2ASessionIsolationTests : IClassFixture<A2AIsolationFactory>
{
    private const string SharedContextId = "shared-wire-context";
    private readonly A2AIsolationFactory _factory;

    public A2ASessionIsolationTests(A2AIsolationFactory factory) => _factory = factory;

    [Fact]
    public void ProductionComposition_RegistersClaimsIsolationProvider()
    {
        _ = _factory.CreateClient();

        _factory.Services.GetService<AgentIsolationKeyProvider>().Should().NotBeNull(
            "AddA2AAgents çok kullanıcılı A2A host'u için claims tabanlı izolasyonu kaydetmeli");
    }

    [Fact]
    public async Task SameContextId_FromDifferentPrincipals_ReachesBackingStoreWithDifferentKeys()
    {
        var first = SubjectClient("partner-a", "1027");
        var second = SubjectClient("partner-b", "1027");

        var firstResponse = await SendAsync(first, "m1");
        var secondResponse = await SendAsync(second, "m2");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var observed = _factory.SessionStore.ObservedKeys
            .Where(key => key.SessionId == SharedContextId)
            .Select(key => key.Partitions?.GetValueOrDefault("isolation"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        observed.Should().HaveCount(2,
            "aynı wire contextId farklı NameIdentifier claim'leriyle aynı store anahtarına düşmemeli");
        observed.Should().Contain("a2a:partner-a:1027");
        observed.Should().Contain("a2a:partner-b:1027");
    }

    private HttpClient SubjectClient(string partnerId, string customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var subjectId = A2ASubjectIdentity.BuildId(partnerId, customerId);
        var user = new UserInfo(
            Id: subjectId,
            Username: $"a2a:{partnerId}",
            PasswordHash: "",
            Role: A2ARoles.Subject,
            LinkedAgentId: null,
            IsActive: true,
            CreatedAt: DateTime.UtcNow,
            LastLoginAt: null,
            LinkedCustomerId: customerId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GenerateAccessToken(user, DateTime.UtcNow).Token);
        client.DefaultRequestHeaders.TryAddWithoutValidation("A2A-Version", "1.0");
        return client;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string messageId) =>
        client.PostAsJsonAsync("/a2a/order", new
        {
            jsonrpc = "2.0",
            id = messageId,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    messageId,
                    contextId = SharedContextId,
                    parts = new[] { new { text = "test" } }
                }
            }
        }, TestContext.Current.CancellationToken);
}
