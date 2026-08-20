// Tests/A2AEndpointsTests.cs
//
// A2A kanalı AÇIKKEN uygulamanın gerçekten ayağa kalktığını ve yetki sınırlarının
// endpoint seviyesinde uygulandığını doğrular.
//
// Neden ayrı bir factory: varsayılan yapılandırmada A2A KAPALI (A2A:Enabled=false) ve diğer
// tüm entegrasyon testleri o hâlde koşuyor. Yani ajan kaydı + endpoint mapping yolu hiç
// çalıştırılmıyor — bu yol bir DI hatası içerse (ki geliştirme sırasında gerçekten içerdi:
// A2ATokenExchangeService singleton kaydedilmişti ama scoped bir bağımlılığı vardı) hiçbir
// test bunu görmezdi. Buradaki factory kanalı açar ve host'u o hâlde başlatır.

using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Api.IntegrationTests;

public sealed class A2AEnabledFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("A2A:Enabled", "true");
        builder.UseSetting("A2A:SubjectTokenMinutes", "5");
        // Partner tanımı bilinçli olarak BOŞ bırakılıyor: varsayılanın reddetmek olduğunu
        // doğrulayan testler bunu kullanıyor.
    }
}

public class A2AEndpointsTests : IClassFixture<A2AEnabledFactory>
{
    private readonly A2AEnabledFactory _factory;
    public A2AEndpointsTests(A2AEnabledFactory factory) => _factory = factory;

    /// <summary>
    /// EN TEMEL KONTROL: kanal açıkken host ayağa kalkıyor mu? Ajan kaydı (AddAIAgent +
    /// AddA2AServer) ve endpoint mapping bu yolda çalışır; bir DI yaşam süresi uyumsuzluğu
    /// veya eksik kayıt burada patlar. İstek yanıtının içeriği değil, host'un başlaması ölçülüyor.
    /// </summary>
    [Fact]
    public async Task Host_StartsSuccessfully_WhenA2AIsEnabled()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/a2a/product", TestContext.Current.CancellationToken);

        // 404 dışında herhangi bir yanıt "host ayakta ve endpoint kayıtlı" demektir.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "A2A açıkken endpoint map edilmiş olmalı");
    }

    [Fact]
    public async Task ProductEndpoint_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/a2a/product", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Sipariş ajanı müşteri verisi döndürür — kimliksiz erişime kesinlikle kapalı olmalı.
    /// </summary>
    [Fact]
    public async Task OrderEndpoint_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/a2a/order", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>Belirtilen rol (ve isteğe bağlı müşteri) için gerçek bir JWT üretir.</summary>
    private string MintToken(string role, string? customerId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: "test-caller", Username: "test-caller", PasswordHash: "", Role: role,
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: customerId);
        return provider.GenerateAccessToken(user, DateTime.UtcNow).Token;
    }

    /// <summary>
    /// Yetkili istemci. <paramref name="a2aVersion"/> varsayılan olarak gönderilir çünkü
    /// spesifikasyon "Clients MUST send the <c>A2A-Version</c> header" der ve her iki binding
    /// bunu zorunlu tutar (bkz. <c>UseA2AProtocolGuards</c>). <c>null</c> geçmek,
    /// başlığı hiç göndermeyen bir istemciyi taklit eder.
    /// </summary>
    private HttpClient ClientWith(string token, string? a2aVersion = "1.0")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (a2aVersion is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("A2A-Version", a2aVersion);
        return client;
    }

    /// <summary>
    /// ASIL GÜVENLİK SINIRI: partner token'ı "hangi sistem" sorusunu cevaplar, "hangi müşteri"
    /// sorusunu DEĞİL. Sipariş ajanı müşteri verisi döndürdüğü için partner token'ıyla
    /// çağrılamamalı — aksi halde müşteri kimliği çağrı gövdesinde parametreye dönerdi ve
    /// sohbet kanalında bilerek kapatılan açık burada yeniden açılırdı.
    /// </summary>
    [Fact]
    public async Task OrderEndpoint_WithPartnerToken_IsRejected()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));

        var response = await client.PostAsJsonAsync("/a2a/order", new { }, TestContext.Current.CancellationToken);

        // partner token'ı müşteri verisine erişememeli — bunun için özne token'ı gerekir
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Özne token'ı müşteri claim'i taşımıyorsa istek reddedilmeli. Boş kimlikle devam etmek,
    /// tool'ların hiçbir şey bulamamasına ve ajanın "siparişiniz yok" demesine yol açardı —
    /// yani altyapı eksiği YANLIŞ OLGUYA dönüşürdü.
    /// </summary>
    [Fact]
    public async Task OrderEndpoint_SubjectTokenWithoutCustomerClaim_IsRejected()
    {
        var client = ClientWith(MintToken(A2ARoles.Subject, customerId: null));

        var response = await client.PostAsJsonAsync("/a2a/order", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Doğru token (özne + müşteri claim'i) yetki katmanını GEÇMELİ. Ajanın kendisi test
    /// ortamında gerçek LLM'e ulaşamayacağı için sonuç 200 olmayabilir; ölçülen şey isteğin
    /// yetkiye takılmamasıdır.
    /// </summary>
    [Fact]
    public async Task OrderEndpoint_WithValidSubjectToken_PassesAuthorization()
    {
        var client = ClientWith(MintToken(A2ARoles.Subject, customerId: "1027"));

        var response = await client.PostAsJsonAsync("/a2a/order", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Ürün ajanı katalog sorgular; partner token'ı yeterli olmalı.</summary>
    [Fact]
    public async Task ProductEndpoint_WithPartnerToken_PassesAuthorization()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));

        var response = await client.PostAsJsonAsync("/a2a/product", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ═══ AgentCard — A2A'nın keşif yarısı ═══

    /// <summary>
    /// Kart kimlik doğrulaması İSTEMEMELİ: keşif public'tir, yetenek KULLANIMI değil.
    /// Çağıran, hangi yeteneklerin olduğunu ve hangi kimliğin gerektiğini denemeden öğrenebilmeli.
    /// </summary>
    [Theory]
    [InlineData("/a2a/product")]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task AgentCard_IsPubliclyDiscoverable(string basePath)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"{basePath}/.well-known/agent-card.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "AgentCard kimlik doğrulaması olmadan okunabilmeli");
    }

    /// <summary>
    /// Sipariş kartı, kanalın SALT-OKUNUR olduğunu ilan etmeli — çağıran sistem sipariş
    /// oluşturmayı deneyip başarısız olmak yerine bunu kartta görmeli.
    /// </summary>
    [Fact]
    public async Task OrderAgentCard_DeclaresReadOnlyScope_AndReadSkills()
    {
        var client = _factory.CreateClient();

        var json = await client.GetStringAsync(
            "/a2a/order/.well-known/agent-card.json", TestContext.Current.CancellationToken);

        json.Should().Contain("order-status").And.Contain("last-order").And.Contain("order-list");
        json.Should().Contain("SALT-OKUNUR", "kapsam sınırı kartta açıkça ilan edilmeli");
    }

    // NOT: Buradaki eski AgentCard_DoesNotAdvertiseUnsupportedCapabilities testi KALDIRILDI.
    // Yerine geçen AgentCard_DeclaresCapabilities_Explicitly onu tümüyle KAPSIYOR: üç kartın
    // hepsine bakıyor (eski test yalnızca ürüne bakıyordu), JSON ayrıştırıyor (eski test ham
    // metinde string arıyordu — biçimlendirme değişse sessizce yanlış geçerdi) ve üç bayrağın
    // da TAM değerini doğruluyor ("true değil" demekle yetinmiyor).
    // Test, streaming'in gerçekte çalıştığı ölçülünce kırmızıya döndü; yeşile boyamak için
    // değil, daha güçlüsü tarafından gereksiz kılındığı için silindi.

    /// <summary>Şikayet ajanı da müşteri verisi döndürür — partner token'ıyla çağrılamamalı.</summary>
    [Fact]
    public async Task ComplaintEndpoint_WithPartnerToken_IsRejected()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));

        var response = await client.PostAsJsonAsync("/a2a/complaint", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>Şikayet kartı, kayıt oluşturmanın bu kanalda OLMADIĞINI ilan etmeli.</summary>
    [Fact]
    public async Task ComplaintAgentCard_DeclaresNoRegistrationCapability()
    {
        var client = _factory.CreateClient();

        var json = await client.GetStringAsync(
            "/a2a/complaint/.well-known/agent-card.json", TestContext.Current.CancellationToken);

        json.Should().Contain("complaint-status").And.Contain("complaint-list");
        json.Should().Contain("SALT-OKUNUR");
        json.Should().NotContain("complaint_registration_tool");
    }

    // ═══ İki transport da AYNI korumalara tabi olmalı ═══
    //
    // A2A hem JSON-RPC hem HTTP+JSON binding'i tanımlar ve ikisi de yayınlanıyor. Yetki
    // kontrolü yalnızca birinde uygulansaydı, diğeri tüm korumaları baypas eden açık bir
    // kapı olurdu — üstelik kart HTTP+JSON'ı ilan ettiği için istemciler ONU tercih eder.

    /// <summary>
    /// Kart, gerçekten yayınlanan transport'ları ilan etmeli. SDK istemcisi binding'i
    /// karttan seçer; ilan edilen ama yayınlanmayan bir transport sessiz bağlantı hatası olur.
    /// </summary>
    [Theory]
    [InlineData("/a2a/product")]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task AgentCard_DeclaresBothPublishedTransports(string basePath)
    {
        var client = _factory.CreateClient();

        var json = await client.GetStringAsync(
            $"{basePath}/.well-known/agent-card.json", TestContext.Current.CancellationToken);

        json.Should().Contain("supportedInterfaces");
        json.Should().Contain(A2A.ProtocolBindingNames.JsonRpc);
        json.Should().Contain(A2A.ProtocolBindingNames.HttpJson);
        json.Should().Contain(basePath, "her binding gerçek yolu göstermeli");
    }

    /// <summary>
    /// Müşteri verisi döndüren ajanlar HER İKİ transport'ta da partner token'ını reddetmeli.
    /// HTTP+JSON yolu korumasız kalsaydı, kart onu tercih ettiği için sızıntı VARSAYILAN yol olurdu.
    /// </summary>
    [Theory]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task HttpJsonTransport_WithPartnerToken_IsAlsoRejected(string path)
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));

        // Yol UYDURULMADI: kayıtlı endpoint'ler EndpointDataSource ile listelenerek doğrulandı
        // (HTTP+JSON binding'i "{base}/message:send" kullanıyor). Yanlış bir yol yazılsaydı test
        // her durumda 404 alır ve korumalı/korumasız ayrımını hiç ölçemezdi.
        var response = await client.PostAsJsonAsync(
            $"{path}/message:send", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>Kimliksiz çağrı her iki transport'ta da reddedilmeli.</summary>
    [Theory]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task HttpJsonTransport_WithoutToken_IsRejected(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"{path}/message:send", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Yetenek bayrakları <c>null</c> DEĞİL, açıkça yazılmalı. <c>null</c> "bilinmiyor" demektir
    /// ve istemciyi denemeye teşvik eder.
    ///
    /// <para>
    /// <c>false</c> ilan edilenlerin fiilî bir kapı olduğu ölçüldü: push notification isteği SDK
    /// tarafından <c>400 "Push notifications not supported."</c> ile reddediliyor — yani ilan
    /// yalnızca belge değil, çalışan bir kısıt (bkz. <see cref="PushNotificationConfig_IsRejected_NotAcceptedAsWebhook"/>).
    /// </para>
    ///
    /// <para>
    /// <c>streaming</c> uzun süre yanlışlıkla <c>false</c> ilan edildi: yol 0.2 biçimli gövdeyle
    /// denenip 500 alınmış ve desteklenmiyor sanılmıştı. Doğru 1.0 biçimiyle çalışıyor — bu yüzden
    /// bu test artık ilanı DAVRANIŞA bağlıyor (aşağıdaki iki test).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("/a2a/product")]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task AgentCard_DeclaresCapabilities_Explicitly(string basePath)
    {
        var client = _factory.CreateClient();

        var json = await client.GetStringAsync(
            $"{basePath}/.well-known/agent-card.json", TestContext.Current.CancellationToken);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var caps = doc.RootElement.GetProperty("capabilities");

        caps.GetProperty("streaming").GetBoolean().Should().BeTrue(
            "her iki transport'ta da streaming çalışıyor; çalışan bir yeteneği false ilan etmek "
          + "keşifle bulunamayan ama çağrılabilen bir yüzey bırakırdı");
        caps.GetProperty("pushNotifications").GetBoolean().Should().BeFalse();
        caps.GetProperty("extendedAgentCard").ValueKind.Should().NotBe(
            System.Text.Json.JsonValueKind.Null, "null 'bilinmiyor' demektir; açıkça false olmalı");
    }

    /// <summary>
    /// Kart <c>streaming: true</c> diyorsa JSON-RPC <c>SendStreamingMessage</c> GERÇEKTEN
    /// akış döndürmeli. Bu test ilanı davranışa bağlar: biri değişip diğeri kalırsa kart yalan
    /// söylemeye başlar ve bunu ancak dış bir partner keşfeder.
    /// </summary>
    [Fact]
    public async Task JsonRpcStreaming_Works_AsCardDeclares()
    {
        var client = ClientWith(MintToken(A2ARoles.Subject, customerId: "1027"));

        var response = await client.PostAsJsonAsync("/a2a/order", new
        {
            jsonrpc = "2.0",
            id = "s1",
            method = "SendStreamingMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    messageId = "s1",
                    parts = new[] { new { text = "Son siparişim ne durumda?" } }
                }
            }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("data:", "akış SSE olarak dönmeli");
    }

    /// <summary>
    /// HTTP+JSON tarafındaki karşılığı (<c>message:stream</c>) da çalışmalı. Kart tek bir
    /// <c>streaming</c> bayrağı taşır ama İKİ binding ilan eder; biri çalışıp diğeri çalışmazsa
    /// o bayrak, kartın tercih ettirdiği transport'a göre doğru VEYA yanlış olur.
    /// </summary>
    [Fact]
    public async Task HttpJsonStreaming_Works_AsCardDeclares()
    {
        var client = ClientWith(MintToken(A2ARoles.Subject, customerId: "1027"));

        var response = await client.PostAsJsonAsync("/a2a/order/message:stream", new
        {
            message = new
            {
                role = "ROLE_USER",
                messageId = "hs1",
                parts = new[] { new { text = "Son siparişim ne durumda?" } }
            }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("data:", "akış SSE olarak dönmeli");
    }

    /// <summary>
    /// Push notification kaydı GERÇEKTEN reddedilmeli. Kabul edilseydi, çağıranın verdiği
    /// webhook adresine sunucudan istek gitmesi (SSRF) anlamına gelirdi.
    /// </summary>
    [Fact]
    public async Task PushNotificationConfig_IsRejected_NotAcceptedAsWebhook()
    {
        var client = ClientWith(MintToken(A2ARoles.Subject, customerId: "1027"));

        var response = await client.GetAsync(
            "/a2a/order/tasks/abc/pushNotificationConfigs", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "push notification desteklenmiyor olarak ilan edildi; kabul edilmesi SSRF yüzeyi açardı");
    }

    [Fact]
    public async Task TokenExchange_WithoutPartnerToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/a2a/token-exchange", new { customerId = "1027" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ═══ Protokol sürümü — iki binding aynı 1.0-only politikasına tabidir ═══

    [Theory]
    [InlineData(null, "0.3")]
    [InlineData("0.3", "0.3")]
    [InlineData("2.0", "2.0")]
    public async Task JsonRpc_UnsupportedVersion_ReturnsVersionNotSupportedEnvelope(
        string? requestedVersion,
        string effectiveVersion)
    {
        var client = ClientWith(MintToken(A2ARoles.Partner), requestedVersion);

        var response = await client.PostAsJsonAsync("/a2a/product", new
        {
            jsonrpc = "2.0",
            id = "version-probe",
            method = "UnknownMethod",
            @params = new { }
        }, TestContext.Current.CancellationToken);

        // JSON-RPC protokol hataları HTTP durumuna değil JSON-RPC error alanına taşınır.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        using var body = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        body.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32009);
        body.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain(effectiveVersion);
    }

    [Fact]
    public async Task JsonRpc_Version10_ReachesTheSdkProcessor()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner), "1.0");

        var response = await client.PostAsJsonAsync("/a2a/product", new
        {
            jsonrpc = "2.0",
            id = "version-probe",
            method = "UnknownMethod",
            @params = new { }
        }, TestContext.Current.CancellationToken);

        using var body = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601,
            "1.0 guard'ı geçmeli ve bilinmeyen metodu SDK işlemcisi reddetmeli");
    }

    [Theory]
    [InlineData(null, "0.3")]
    [InlineData("0.3", "0.3")]
    [InlineData("2.0", "2.0")]
    public async Task HttpJson_UnsupportedVersion_ReturnsA2AError(
        string? requestedVersion,
        string effectiveVersion)
    {
        var client = ClientWith(MintToken(A2ARoles.Partner), requestedVersion);

        var response = await client.GetAsync(
            "/a2a/product/card", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/a2a+json");

        using var body = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = body.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(400);
        error.GetProperty("details")[0].GetProperty("reason").GetString()
            .Should().Be("VERSION_NOT_SUPPORTED");
        error.GetProperty("details")[0].GetProperty("metadata")
            .GetProperty("requestedVersion").GetString().Should().Be(effectiveVersion);
    }

    [Fact]
    public async Task HttpJson_Version10_ReachesTheSdkProcessor()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner), "1.0");

        var response = await client.GetAsync(
            "/a2a/product/card", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/a2a+json");
    }

    // ═══ Girdi sınırları — istek SAYISI sınırı "ne kadar" sorusunu kapsamaz ═══

    /// <summary>
    /// Gövde sınırının gerçekten UYGULANDIĞINI ve hangi durum kodunu döndürdüğünü ölçer.
    ///
    /// <para>
    /// Bu test bir varsayımı çürüttü: sınır önce yalnızca
    /// <c>IHttpMaxRequestBodySizeFeature</c> üzerinden kuruluyordu ve o özellik burada
    /// <b>null</b> dönüyor (ölçüldü) — yani sınır sessizce etkisizdi ve istek 200 ile
    /// geçiyordu. Şimdi asıl kontrol <c>Content-Length</c>'tir; yapılandırmada görünen ama
    /// hiçbir şey yapmayan bir güvenlik ayarı, hiç olmamasından kötüdür.
    /// Belgede yazan durum kodu da buradan gelir, tahminden değil.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Request_ExceedingBodyLimit_IsRejectedBeforeReachingTheAgent()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));

        // A2A:MaxRequestBytes varsayılanı 64 KB; bunun katbekat üstü.
        var huge = new string('x', 512 * 1024);
        var json = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"SendMessage\",\"params\":{\"message\":{"
                 + "\"messageId\":\"m1\",\"role\":\"ROLE_USER\",\"parts\":[{\"text\":\"" + huge + "\"}]}}}";
        var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/a2a/product", body, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "gövde sınırı ayrıştırma öncesinde uygulanmalı");
    }


    /// <summary>
    /// <b>Content-Length OLMADAN</b> gönderilen (chunked) bir gövde de sınırlanmalı.
    ///
    /// <para>
    /// Bu, gövde sınırının endpoint filtresinden middleware'e taşınmasının asıl sebebidir:
    /// bildirilen uzunluğa bakan bir kontrol, uzunluk bildirmeyen bir istemciyi hiç görmez ve
    /// sınır sessizce atlanır. Burada okunan bayt SAYILIR, yani başlığa güvenilmez.
    /// </para>
    ///
    /// <para>
    /// Bu test bir kez kaybedildi ve yokluğunda akış tabanlı sayımı kaldırmak hiçbir testi
    /// düşürmüyordu (ölçüldü) — yani koruma vardı ama korumasızdı.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ChunkedRequest_ExceedingBodyLimit_IsRejected()
    {
        var client = ClientWith(MintToken(A2ARoles.Partner));
        // ZORUNLU: aksi hâlde HttpClient içeriği tamponlayıp Content-Length yazar ve test,
        // chunked yolu değil yine bildirilen-uzunluk yolunu ölçer (ölçüldü — bu hâliyle
        // akış tabanlı sayımı kaldırmak testi düşürmüyordu).
        client.DefaultRequestHeaders.TransferEncodingChunked = true;

        using var content = new UnknownLengthContent(
            System.Text.Encoding.UTF8.GetBytes(new string('x', 512 * 1024)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync(
            "/a2a/product/message:send", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "Content-Length bildirilmese de sınır uygulanmalı");
    }

    /// <summary>Content-Length bildirmeyen içerik — chunked aktarımı taklit eder.</summary>
    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload, 0, payload.Length);

        // false döndürmek "uzunluğu bilmiyorum" demektir; Content-Length yazılmaz.
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

}

/// <summary>
/// A2A KAPALIYKEN (varsayılan) endpoint'lerin hiç yayınlanmadığını doğrular — kapalı bir
/// kanalın yayında olmaması, yetkiyle engellenmesinden daha güvenlidir.
/// </summary>
public class A2ADisabledByDefaultTests : IClassFixture<A2ADisabledByDefaultTests.DisabledFactory>
{
    /// <summary>
    /// A2A'yı AÇIKÇA kapatan factory.
    ///
    /// <para>
    /// Kapalılığı "varsayılana" bırakmak yetmez: <c>appsettings.Development.json</c> gitignore'lu
    /// ve geliştiriciye özeldir; içinde <c>A2A:Enabled=true</c> olan bir makinede bu test kırmızı,
    /// CI'da yeşil olurdu. Bir GÜVENLİK özelliğini (kapalı kanal hiç yayınlanmamalı) doğrulayan
    /// testin sonucu makineye göre değişemez — bu yüzden ayar burada bilinçli olarak sabitlenir.
    /// </para>
    /// </summary>
    public sealed class DisabledFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("A2A:Enabled", "false");
        }
    }

    private readonly DisabledFactory _factory;
    public A2ADisabledByDefaultTests(DisabledFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/a2a/product")]
    [InlineData("/a2a/order")]
    [InlineData("/a2a/complaint")]
    public async Task Endpoints_AreNotMapped_WhenDisabled(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "kanal kapalıyken endpoint hiç map edilmemeli");
    }

    [Fact]
    public async Task TokenExchange_IsNotMapped_WhenDisabled()
    {
        // Yalnızca ajan uçlarını kaldırmak kanalı KAPATMAZ. Daha önce oluşturulmuş bir Partner
        // hesabı, kanal kapatıldıktan sonra da özne token'ı üretmeye devam edebilirdi; ajanlar
        // 404 döndüğü için veri sızmaz ama "kanal tamamen kapalı" garantisi yanlış olurdu ve
        // kapatma işlemi eksik kalırdı.
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/a2a/token-exchange", new { customerId = "1027" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "token değişimi de kanalın parçasıdır; kapalıyken hiç map edilmemeli");
    }
}
