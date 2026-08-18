// Tests/A2AProtocolConformanceTests.cs
//
// "Gerçekten A2A mı, yoksa A2A'ya BENZEYEN bir şey mi?" sorusunun cevabı.
//
// Bu testler elle JSON kurmaz ve endpoint'e ham HTTP atmaz — protokolün KENDİ SDK istemcisini
// (A2A.A2AClient / A2ACardResolver) kullanır. Elle JSON kursaydık, kendi hatamızı kendi
// beklentimizle doğrulamış olurduk: yanlış bir zarf şekli hem istekte hem beklentide aynı
// yanlışla yazılır ve test yeşil kalırdı. SDK istemcisi bağımsız bir taraftır; round-trip
// başarılıysa iki uç da gerçekten spesifikasyonu konuşuyor demektir.
//
// LLM gerekmez: IChatClient deterministik bir sahte ile değiştirilir. Ölçülen şey ajan zekâsı
// değil, PROTOKOL uyumu.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using A2A;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Api.IntegrationTests;

/// <summary>A2A açık + LLM yerine deterministik sahte istemci.</summary>
public sealed class A2AConformanceFactory : TestWebApplicationFactory
{
    private sealed class EchoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            var text = string.Join(" ", m.Where(x => x.Role == ChatRole.User).Select(x => x.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"echo: {text}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var r = await GetResponseAsync(m, o, ct);
            foreach (var msg in r.Messages) yield return new ChatResponseUpdate(msg.Role, msg.Text);
        }

        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("A2A:Enabled", "true");
        builder.UseSetting("A2A:Partners:0:PartnerId", "test-caller");
        builder.UseSetting("A2A:Partners:0:AllowedCustomerIds:0", "1027");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IChatClient>(new EchoChatClient())));
    }
}

public class A2AProtocolConformanceTests : IClassFixture<A2AConformanceFactory>
{
    private readonly A2AConformanceFactory _factory;
    public A2AProtocolConformanceTests(A2AConformanceFactory factory) => _factory = factory;

    private HttpClient AuthedClient(string role, string? customerId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        // Id ile Username KASITLI OLARAK FARKLI: ikisi aynı olsaydı, endpoint'in hangi
        // claim'i okuduğu test edilemezdi (her iki yanlış seçim de yeşil geçerdi). Gerçek
        // kurulumda Id bir GUID'dir ve yapılandırmadaki partner adıyla asla eşleşmez.
        var user = new UserInfo(
            Id: Guid.NewGuid().ToString("N"),
            Username: "test-caller",
            PasswordHash: "", Role: role, LinkedAgentId: null, IsActive: true,
            CreatedAt: DateTime.UtcNow, LastLoginAt: null, LinkedCustomerId: customerId);
        var token = provider.GenerateAccessToken(user, DateTime.UtcNow).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ═══ Token değişimi — partner kimliği doğru claim'den okunmalı ═══

    /// <summary>
    /// Partner kimliği <b>kullanıcı adından</b> okunmalı, kullanıcı satırının GUID'inden değil.
    ///
    /// <para>
    /// Bu ayrım yapılandırmayı doğrudan etkiler: <c>A2A:Partners</c> altındaki <c>PartnerId</c>
    /// elle yazılır ve rate-limit anahtarında görünür; GUID okunsaydı her yeniden seed'de
    /// değişir, yapılandırma kırılır ve hiçbir partner token alamazdı.
    /// </para>
    ///
    /// <para>
    /// Bu test önce YOKTU ve hata üretimde ortaya çıktı (örnek istemci gerçek uygulamaya karşı
    /// 403 aldı). Eski testler Id ile Username'i aynı verdiği için hangi claim'in okunduğunu
    /// hiç ölçemiyordu.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TokenExchange_ResolvesPartnerId_FromUsername_NotUserGuid()
    {
        var http = AuthedClient(A2ARoles.Partner);   // Id = rastgele GUID, Username = "test-caller"

        var response = await http.PostAsJsonAsync(
            "/auth/a2a/token-exchange", new { customerId = "1027" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            "yapılandırmada izinli olan 'test-caller' KULLANICI ADIDIR; GUID okunsaydı 403 dönerdi");
    }

    /// <summary>Yetkisiz müşteri için değişim yine reddedilmeli — düzeltme kapıyı açmamalı.</summary>
    [Fact]
    public async Task TokenExchange_StillDenies_CustomerOutsideAllowlist()
    {
        var http = AuthedClient(A2ARoles.Partner);

        var response = await http.PostAsJsonAsync(
            "/auth/a2a/token-exchange", new { customerId = "9999" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    // ═══ Kart, protokolün KENDİ çözücüsüyle okunabilmeli ═══

    /// <summary>
    /// A2ACardResolver spesifikasyondaki kanonik yolu (<c>/.well-known/agent-card.json</c>)
    /// kullanır ve kartı kendi şemasına göre deserialize eder. Başarılıysa kart yalnızca
    /// "JSON döndürüyor" değil, <b>protokole uygun şekilde</b> yapılandırılmış demektir.
    /// </summary>
    [Fact]
    public async Task AgentCard_IsResolvable_BySdkCardResolver()
    {
        var http = _factory.CreateClient();
        // Taban KÖK, yol TAM verilir. Bu biçim tahmin değil ÖLÇÜLDÜ: resolver
        // `new Uri(base, path)` standart göreli çözümü uygular, yani taban "/a2a/order" iken
        // son segmenti değiştirip "/a2a/.well-known/..." ister ve 404 alır. Tek host'ta birden
        // çok ajan yayınlandığı için varsayılan kök kartı yoktur — partner tam yolu bilmelidir
        // (bkz. A2A.md → keşif sözleşmesi).
        var resolver = new A2ACardResolver(
            new Uri("http://localhost"), http, "/a2a/order/.well-known/agent-card.json");

        var card = await resolver.GetAgentCardAsync(TestContext.Current.CancellationToken);

        card.Should().NotBeNull();
        card.Name.Should().Be("OrderInfoAgent");
        card.Skills.Should().NotBeEmpty();
        card.SupportedInterfaces.Should().NotBeEmpty("istemci binding'i buradan seçer");
    }

    /// <summary>
    /// Kart, kimlik doğrulama gereksinimini ilan etmeli. Etmezse istemci token'sız çağırır,
    /// 401 alır ve gereksinimi ancak deneme-yanılmayla öğrenir — kartın çözmesi gereken problem budur.
    /// </summary>
    [Fact]
    public async Task AgentCard_DeclaresBearerAuthRequirement()
    {
        var http = _factory.CreateClient();
        var card = await new A2ACardResolver(
                new Uri("http://localhost"), http, "/a2a/order/.well-known/agent-card.json")
            .GetAgentCardAsync(TestContext.Current.CancellationToken);

        card.SecuritySchemes.Should().NotBeNullOrEmpty("ajan bearer token istiyor, kart bunu söylemeli");
        card.SecuritySchemes!.Values.Should().Contain(v =>
            v.HttpAuthSecurityScheme != null && v.HttpAuthSecurityScheme.Scheme == "bearer");
        card.SecurityRequirements.Should().NotBeNullOrEmpty("şema tanımlı ama zorunlu değilse istemci atlayabilir");
    }

    // ═══ Uçtan uca gerçek protokol turu ═══

    /// <summary>
    /// EN GÜÇLÜ KANIT: protokolün kendi istemcisi, bizim endpoint'imize gerçek bir A2A
    /// <c>message/send</c> isteği geçiriyor ve yanıt A2A zarfında geri geliyor. Zarf şekli,
    /// metot adı ve alan adları elle yazılmadı — iki uç da SDK'nın anladığı protokolü konuşuyor.
    /// </summary>
    [Fact]
    public async Task MessageSend_RoundTrips_ThroughRealA2AClient()
    {
        var http = AuthedClient(A2ARoles.Partner);
        var client = new A2AClient(new Uri("http://localhost/a2a/product"), http);

        var response = await client.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText("Çay fiyatı nedir?")]
                }
            },
            TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        var text = string.Join(" ", (response.Message?.Parts ?? []).Select(p => p.Text));
        text.Should().NotBeNullOrWhiteSpace("ajan yanıtı A2A zarfında geri taşınmalı");
    }

    /// <summary>
    /// Müşteri verisi döndüren ajan, protokolün kendi istemcisiyle çağrıldığında da özne
    /// token'ı istemeli — koruma yalnızca ham HTTP'de değil, gerçek istemci yolunda da geçerli.
    /// </summary>
    [Fact]
    public async Task OrderAgent_ViaRealA2AClient_RejectsPartnerToken()
    {
        var http = AuthedClient(A2ARoles.Partner);
        var client = new A2AClient(new Uri("http://localhost/a2a/order"), http);

        var act = async () => await client.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText("Siparişlerimi listele")]
                }
            },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>("partner token'ı müşteri verisine erişememeli");
    }

    /// <summary>Özne token'ıyla aynı çağrı protokol katmanını geçmeli.</summary>
    [Fact]
    public async Task OrderAgent_ViaRealA2AClient_AcceptsSubjectToken()
    {
        var http = AuthedClient(A2ARoles.Subject, customerId: "1027");
        var client = new A2AClient(new Uri("http://localhost/a2a/order"), http);

        var response = await client.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText("Son siparişim ne durumda?")]
                }
            },
            TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════
// A2ASubjectScopeFilter'ın kurduğu ambient kimlik, ajan/tool GERÇEKTEN çalıştığı anda hâlâ
// ayakta mı? SendMessage (akışsız) ve SendStreamingMessage (akış) için bu an FARKLI zamanlarda
// gerçekleşir (bkz. A2AEndpoints.cs — A2ASubjectScopeFilter.ScopedResult üzerindeki not) ve
// bu fark yalnızca CANLI ölçümle bulundu: SendMessage doğru müşteriyi görüyordu,
// SendStreamingMessage "kimlik doğrulama bilgisi iletilmedi" diyordu. EchoChatClient (yukarıda)
// bunu ölçemez çünkü ambient bağlama hiç bakmıyor — bu yüzden ayrı bir sahte istemci gerekiyor.
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>A2A:Enabled + ajanın gördüğü ambient customerId'yi yanıta gömen sahte istemci.</summary>
public sealed class A2AScopeTimingFactory : TestWebApplicationFactory
{
    private sealed class ScopeEchoChatClient(IApprovalContextAccessor accessor) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            var customerId = accessor.Context?.CustomerId ?? "YOK";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"customerId={customerId}")));
        }

        // Akışa ÖZEL bir gecikme EKLEMİYORUZ: JsonRpcStreamedResult.ExecuteAsync'in filtre
        // zincirinden SONRA çalıştığı gerçek zamanlamayı TAKLİT etmiyoruz, ÖLÇÜYORUZ — sahte
        // bir gecikme eklesek, "scope hâlâ ayakta mı" sorusunu test değil biz cevaplamış oluruz.
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var r = await GetResponseAsync(m, o, ct);
            foreach (var msg in r.Messages) yield return new ChatResponseUpdate(msg.Role, msg.Text);
        }

        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("A2A:Enabled", "true");
        builder.UseSetting("A2A:Partners:0:PartnerId", "test-caller");
        builder.UseSetting("A2A:Partners:0:AllowedCustomerIds:0", "1027");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IChatClient>(
            sp => new ScopeEchoChatClient(sp.GetRequiredService<IApprovalContextAccessor>()))));
    }
}

public class A2AScopeTimingTests : IClassFixture<A2AScopeTimingFactory>
{
    private readonly A2AScopeTimingFactory _factory;
    public A2AScopeTimingTests(A2AScopeTimingFactory factory) => _factory = factory;

    private HttpClient AuthedClient(string role, string? customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: Guid.NewGuid().ToString("N"), Username: "test-caller", PasswordHash: "", Role: role,
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: customerId);
        var token = provider.GenerateAccessToken(user, DateTime.UtcNow).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static SendMessageRequest Ask(string text) => new()
    {
        Message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = [Part.FromText(text)]
        }
    };

    /// <summary>
    /// SendMessage'da ajan, filtre zinciri DÖNMEDEN önce senkron çalışır — scope o an ayakta.
    /// </summary>
    [Fact]
    public async Task SendMessage_AgentSeesAmbientCustomerId()
    {
        var http = AuthedClient(A2ARoles.Subject, "1027");
        var client = new A2AClient(new Uri("http://localhost/a2a/order"), http);

        var response = await client.SendMessageAsync(Ask("test"), TestContext.Current.CancellationToken);

        var text = string.Join("", response.Message!.Parts.Select(p => p.Text));
        text.Should().Be("customerId=1027",
            "A2ASubjectScopeFilter, SendMessage'ı sarmalayan \"using\" bloğu içinde çalışırken kurulur");
    }

    /// <summary>
    /// SendStreamingMessage'da ajan, filtre zinciri TAMAMEN döndükten sonra, SSE gövdesi
    /// yazılırken (JsonRpcStreamedResult.ExecuteAsync içinde) çalışır. Bu test, A2AEndpoints.cs'teki
    /// A2ASubjectScopeFilter.ScopedResult sarmalayıcısının GERÇEKTEN gerekli olduğunu kanıtlar —
    /// sarmalayıcı kaldırılırsa bu test "customerId=YOK" görüp kırmızıya döner (mutasyonla
    /// doğrulandı).
    /// </summary>
    [Fact]
    public async Task SendStreamingMessage_AgentSeesAmbientCustomerId()
    {
        var http = AuthedClient(A2ARoles.Subject, "1027");
        var client = new A2AClient(new Uri("http://localhost/a2a/order"), http);

        var parts = new List<string>();
        await foreach (var evt in client.SendStreamingMessageAsync(Ask("test"), TestContext.Current.CancellationToken))
        {
            if (evt.Message is { } msg)
                parts.AddRange(msg.Parts.Select(p => p.Text));
        }

        string.Join("", parts).Should().Be("customerId=1027",
            "A2ASubjectScopeFilter.ScopedResult, IResult.ExecuteAsync (SSE yazımı) sırasında da scope kurmalı");
    }
}
