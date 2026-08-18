// Api/Endpoints/A2AEndpoints.cs
// A2A (Agent2Agent) ajanlarının dış sistemlere yayınlanması.

using CustomerSupportBot.Adapters.Agents.A2A;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using A2A.AspNetCore;
// A2A protokol SDK'sı ile MAF'ın kendi tipleri aynı adları taşıyor (AgentCard, AgentSkill,
// AgentCapabilities). Burada kastedilen PROTOKOL tarafıdır — kartlar dış çağırana yayınlanıyor.
using AgentCard = A2A.AgentCard;
using AgentSkill = A2A.AgentSkill;
using AgentCapabilities = A2A.AgentCapabilities;
using AgentInterface = A2A.AgentInterface;
using ProtocolBindingNames = A2A.ProtocolBindingNames;
using SecurityScheme = A2A.SecurityScheme;
using HttpAuthSecurityScheme = A2A.HttpAuthSecurityScheme;
using SecurityRequirement = A2A.SecurityRequirement;
using StringList = A2A.StringList;

namespace CustomerSupportBot.Api.Endpoints;

public static class A2AEndpoints
{
    /// <summary>
    /// A2A JSON-RPC endpoint'lerini yayınlar.
    ///
    /// <para>
    /// <b>Yetki ayrımı bilinçli:</b> ürün ajanı katalog sorgular, müşteri kimliği gerektirmez —
    /// partner token'ı yeterlidir. Sipariş ajanı ise müşteri verisi döndürür ve
    /// <c>A2ASubject</c> (tek müşteriye kilitli, değişimle üretilmiş) token ister; partner
    /// token'ıyla ÇAĞRILAMAZ. Böylece "hangi sistem" ile "hangi müşteri" soruları ayrı
    /// token'larda kalır.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapA2AAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // İKİ transport da yayınlanır. A2A spesifikasyonu birden fazla binding tanımlar ve
        // SDK istemcisi hangisini kullanacağını AgentCard'dan seçer — varsayılan tercihi
        // HTTP+JSON'dır (bkz. A2AClient.CreateFromCard). Yalnızca JSON-RPC yayınlansaydı,
        // kartı okuyup HTTP+JSON deneyen istemciler bağlanamazdı.
        //
        // Yetki/rate-limit/scope zinciri İKİ yolda da AYNI uygulanmalı: bir transport'ta
        // korumanın atlanması, diğerindeki tüm kontrolleri anlamsız kılardı.
        app.MapA2AJsonRpc(A2AAgentNames.Product, "/a2a/product")
            .RequireAuthorization("Partner")
            .RequireRateLimiting("a2a");
        app.MapA2AHttpJson(A2AAgentNames.Product, "/a2a/product")
            .RequireAuthorization("Partner")
            .RequireRateLimiting("a2a");

        app.MapA2AJsonRpc(A2AAgentNames.Order, "/a2a/order")
            .RequireAuthorization("A2ASubject")
            .RequireRateLimiting("a2a")
            .AddEndpointFilter(new A2ASubjectScopeFilter());
        app.MapA2AHttpJson(A2AAgentNames.Order, "/a2a/order")
            .RequireAuthorization("A2ASubject")
            .RequireRateLimiting("a2a")
            .AddEndpointFilter(new A2ASubjectScopeFilter());

        // Şikayet ajanı da müşteri verisi döndürür — sipariş ajanıyla AYNI kimlik şartına tabi.
        app.MapA2AJsonRpc(A2AAgentNames.Complaint, "/a2a/complaint")
            .RequireAuthorization("A2ASubject")
            .RequireRateLimiting("a2a")
            .AddEndpointFilter(new A2ASubjectScopeFilter());
        app.MapA2AHttpJson(A2AAgentNames.Complaint, "/a2a/complaint")
            .RequireAuthorization("A2ASubject")
            .RequireRateLimiting("a2a")
            .AddEndpointFilter(new A2ASubjectScopeFilter());

        // AgentCard — A2A'nın keşif yarısı. Kart olmadan çağıran, ajanın hangi yetenekleri
        // olduğunu ve hangi kimlik doğrulamasını beklediğini deneyerek öğrenmek zorunda kalır.
        // Kartlar kimlik doğrulaması İSTEMEZ: keşif public'tir, yetenek KULLANIMI değil.
        var publicBaseUrl = app.ServiceProvider
            .GetRequiredService<IOptions<A2AOptions>>().Value.PublicBaseUrl;

        app.MapWellKnownAgentCard(BuildProductCard(publicBaseUrl), "/a2a/product");
        app.MapWellKnownAgentCard(BuildOrderCard(publicBaseUrl), "/a2a/order");
        app.MapWellKnownAgentCard(BuildComplaintCard(publicBaseUrl), "/a2a/complaint");

        return app;
    }

    /// <summary>
    /// Ajanın desteklediği transport'ları ilan eder.
    ///
    /// <para>
    /// <b>Boş bırakılamaz:</b> SDK istemcisi (<c>A2AClient.CreateFromCard</c>) bağlanacağı
    /// binding'i bu listeden seçer ve <b>listedeki sıraya uyar</b> (ilk giren tercih edilir).
    /// Liste boş olsaydı istemci hangi transport'u deneyeceğini bilemez, kendi varsayılanına
    /// (HTTP+JSON) düşer ve o yol yayınlanmamışsa bağlantı sessizce başarısız olurdu.
    /// </para>
    /// </summary>
    private static List<AgentInterface> SupportedTransports(string basePath, string publicBaseUrl)
    {
        // Yapılandırılmışsa MUTLAK adres ilan edilir. Göreli URL, kartı okuyan dış istemcinin
        // adresi kendi başına çözmesini gerektirir ve proxy/gateway arkasında yanlış sonuç verir.
        var url = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? basePath
            : $"{publicBaseUrl.TrimEnd('/')}{basePath}";

        return
        [
            new AgentInterface { ProtocolBinding = ProtocolBindingNames.JsonRpc,  Url = url },
            new AgentInterface { ProtocolBinding = ProtocolBindingNames.HttpJson, Url = url }
        ];
    }

    /// <summary>Kartta ilan edilen kimlik şemasının adı — şema tanımı ile gereksinim aynı adı kullanır.</summary>
    private const string BearerSchemeName = "bearer";

    /// <summary>
    /// Ajanın kabul ettiği kimlik doğrulama şeması.
    ///
    /// <para>
    /// <b>Bunu ilan etmemek AgentCard'ın var olma sebebine aykırıdır.</b> Kart, çağıranın
    /// yetenekleri VE nasıl kimlik doğrulayacağını <i>denemeden</i> öğrenmesi içindir. Şema
    /// ilan edilmezse istemci token göndermeden çağırır, 401 alır ve gereksinimi ancak
    /// deneme-yanılma ile keşfeder — kartın çözmesi gereken problemin ta kendisi.
    /// </para>
    /// </summary>
    private static Dictionary<string, SecurityScheme> BearerScheme(string tokenDescription) => new()
    {
        // SchemeCase ATANMAZ: salt-okunurdur ve hangi alt şema doldurulduysa ondan türer.
        [BearerSchemeName] = new SecurityScheme
        {
            HttpAuthSecurityScheme = new HttpAuthSecurityScheme
            {
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = tokenDescription
            }
        }
    };

    /// <summary>Şemanın zorunlu olduğunu bildirir (kapsam listesi boş — rol tabanlı yetkilendirme).</summary>
    private static List<SecurityRequirement> BearerRequired() =>
    [
        new SecurityRequirement { Schemes = new Dictionary<string, StringList> { [BearerSchemeName] = new StringList() } }
    ];

    /// <summary>
    /// Kart yetenekleri. Her bayrak AÇIKÇA yazılır — <c>null</c> bırakmak "bilinmiyor" anlamına
    /// gelir ve istemciyi denemeye teşvik eder.
    ///
    /// <para>
    /// <b><c>Streaming = true</c>:</b> köprü, streaming metotlarını (<c>SendStreamingMessage</c> ve
    /// HTTP+JSON <c>message:stream</c>) map ediyor ve İKİSİ DE çalışıyor — ölçüldü: sırasıyla 45 ve
    /// 53 SSE olayı, içerik akışsız yanıtla aynı. Uzun süre <c>false</c> ilan edilmesinin sebebi bir
    /// ölçüm hatasıydı: yol, 0.2 biçimli gövdeyle (<c>role:"user"</c>, <c>kind:"text"</c>) denenip
    /// 500 alınmış ve "desteklenmiyor" sanılmıştı; doğru 1.0 proto adlandırmasıyla 200 dönüyor.
    /// Çalışan bir yeteneği <c>false</c> ilan etmek, kartın var olma sebebine aykırıdır: keşifle
    /// bulunamayan ama çağrılabilen bir yüzey bırakırdı. Kimlik akışı bu yolda da doğru kurulur —
    /// bkz. <c>A2ASubjectScopeFilter.ScopedResult</c> ve <c>A2AScopeTimingTests</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>PushNotifications</c> / <c>ExtendedAgentCard</c> = false:</b> bunlar gerçekten kapalı.
    /// Ölçüldü: push notification isteği <c>400 "Push notifications not supported."</c> alıyor —
    /// yani ilan yalnızca belge değil, fiilî bir kapı. Push notification ayrıca istemcinin
    /// verdiği adrese sunucudan istek gitmesi (SSRF) anlamına geleceği için kapalı KALMALIDIR.
    /// </para>
    /// </summary>
    private static AgentCapabilities CardCapabilities() =>
        new() { Streaming = true, PushNotifications = false, ExtendedAgentCard = false };

    private static AgentCard BuildProductCard(string publicBaseUrl) => new()
    {
        SupportedInterfaces = SupportedTransports("/a2a/product", publicBaseUrl),
        SecuritySchemes = BearerScheme(
            "Partner makine kimliğiyle alınmış erişim token'ı. Ürün kataloğu müşteri kimliği gerektirmez."),
        SecurityRequirements = BearerRequired(),
        Name = A2AAgentNames.Product,
        Description = "Ürün kataloğu sorguları: fiyat, stok durumu, kategori listeleri. Müşteri kimliği gerektirmez.",
        Version = "1.0.0",
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = CardCapabilities(),
        Skills =
        [
            new AgentSkill
            {
                Id = "product-lookup",
                Name = "Ürün sorgulama",
                Description = "Belirli bir ürünün fiyat ve stok bilgisini döner.",
                Tags = ["product", "catalog", "price", "stock"],
                Examples = ["Çay fiyatı nedir?", "Zeytinyağı stokta var mı?"]
            },
            new AgentSkill
            {
                Id = "product-list",
                Name = "Kategori listeleme",
                Description = "Bir kategorideki ürünleri listeler.",
                Tags = ["product", "catalog", "category"],
                Examples = ["İçecekler kategorisinde neler var?"]
            }
        ]
    };

    private static AgentCard BuildOrderCard(string publicBaseUrl) => new()
    {
        SupportedInterfaces = SupportedTransports("/a2a/order", publicBaseUrl),
        SecuritySchemes = BearerScheme(
            "Token değişimiyle (/auth/a2a/token-exchange) alınmış, TEK MÜŞTERİYE kilitli kısa ömürlü token. " +
            "Partner token'ı bu ajan için yeterli DEĞİLDİR."),
        SecurityRequirements = BearerRequired(),
        Name = A2AAgentNames.Order,
        Description =
            "Sipariş bilgisi (SALT-OKUNUR): durum sorgulama, son sipariş, sipariş listesi. " +
            "Tek bir müşteriye kilitli, değişimle üretilmiş token gerektirir; sipariş oluşturma/" +
            "iptal/iade bu kanalda YOKTUR.",
        Version = "1.0.0",
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = CardCapabilities(),
        Skills =
        [
            new AgentSkill
            {
                Id = "order-status",
                Name = "Sipariş durumu",
                Description = "Verilen sipariş numarasının durumunu döner. Yalnızca token'daki müşteriye ait siparişler görünür.",
                Tags = ["order", "status", "read-only"],
                Examples = ["1057 numaralı siparişimin durumu nedir?"]
            },
            new AgentSkill
            {
                Id = "last-order",
                Name = "Son sipariş",
                Description = "Müşterinin en son siparişini döner.",
                Tags = ["order", "read-only"],
                Examples = ["Son siparişim ne durumda?"]
            },
            new AgentSkill
            {
                Id = "order-list",
                Name = "Sipariş listesi",
                Description = "Müşterinin sipariş geçmişini döner.",
                Tags = ["order", "history", "read-only"],
                Examples = ["Tüm siparişlerimi listele."]
            }
        ]
    };

    private static AgentCard BuildComplaintCard(string publicBaseUrl) => new()
    {
        SupportedInterfaces = SupportedTransports("/a2a/complaint", publicBaseUrl),
        SecuritySchemes = BearerScheme(
            "Token değişimiyle (/auth/a2a/token-exchange) alınmış, TEK MÜŞTERİYE kilitli kısa ömürlü token. " +
            "Partner token'ı bu ajan için yeterli DEĞİLDİR."),
        SecurityRequirements = BearerRequired(),
        Name = A2AAgentNames.Complaint,
        Description =
            "Şikayet bilgisi (SALT-OKUNUR): durum sorgulama ve şikayet listesi. " +
            "Tek bir müşteriye kilitli, değişimle üretilmiş token gerektirir; " +
            "şikayet KAYDI bu kanalda YOKTUR.",
        Version = "1.0.0",
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = CardCapabilities(),
        Skills =
        [
            new AgentSkill
            {
                Id = "complaint-status",
                Name = "Şikayet durumu",
                Description = "Verilen şikayet numarasının durumunu döner. Yalnızca token'daki müşteriye ait şikayetler görünür.",
                Tags = ["complaint", "status", "read-only"],
                Examples = ["1005 numaralı şikayetim ne durumda?"]
            },
            new AgentSkill
            {
                Id = "complaint-list",
                Name = "Şikayet listesi",
                Description = "Müşterinin tüm şikayetlerini döner.",
                Tags = ["complaint", "history", "read-only"],
                Examples = ["Şikayetlerimi listele."]
            }
        ]
    };

    /// <summary>
    /// Ajanı çalıştırmadan ÖNCE ambient müşteri bağlamını kurar.
    ///
    /// <para>
    /// <b>Bu filtre olmadan sipariş ajanı sessizce işe yaramaz:</b> sipariş tool'ları müşteri
    /// kimliğini LLM'den değil <c>IApprovalContextAccessor</c>'dan alır (bkz.
    /// <c>ApprovalGateService.CurrentCustomerId</c>). Bağlam kurulmazsa kimlik boş string olur,
    /// sorgular hiçbir şey bulamaz ve ajan "siparişiniz yok" der — altyapı eksiği <b>yanlış
    /// olguya</b> dönüşür. Sohbet ve sesli kanallar aynı işi <c>ChatPortService</c> /
    /// <c>RealtimeBridgeService</c> içinde yapıyor; A2A'nın karşılığı burasıdır.
    /// </para>
    ///
    /// <para>
    /// Kimlik <b>token'dan</b> okunur, istek gövdesinden değil. Değişim akışı müşteri kimliğini
    /// imzalı token'ın içine koyduğu için (bkz. <c>A2ATokenExchangeService</c>) çağıran onu
    /// değiştiremez. Claim beklenmedik şekilde yoksa istek reddedilir — boş kimlikle devam
    /// etmek, yanlış "veri yok" cevabı üretmekten daha kötüdür.
    /// </para>
    /// </summary>
    private sealed class A2ASubjectScopeFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var customerId = http.User.FindFirst("linked_customer_id")?.Value;

            if (string.IsNullOrWhiteSpace(customerId))
                return Results.Forbid();

            var accessor = http.RequestServices.GetRequiredService<IApprovalContextAccessor>();

            // Scope İKİ YERDE de kurulmak zorunda — biri diğerinin yerini TUTMAZ:
            //
            // 1) "using" ile next(context)'i sarmak: SendMessage (akışsız) ajanı burada, filtre
            //    zinciri dönmeden ÖNCE, tam olarak çalıştırıp bitirir (ölçüldü: A2AJsonRpcProcessor.
            //    SingleResponseAsync, IA2ARequestHandler.SendMessageAsync'i await ediyor ve JsonRpcResponse
            //    zaten dolu döner). Bu sarmalama OLMADAN scope hiç kurulmaz ve SendMessage de "kimlik
            //    doğrulama bilgisi iletilmedi" der — bunu az önce ölçerek doğruladım (kendi hatam).
            //
            // 2) Dönen IResult'ı ScopedResult ile sarmak: SendStreamingMessage (akış) buradan FARKLI
            //    çalışır — IResult (JsonRpcStreamedResult) hemen döner, gerçek ajan/tool çalışması SSE
            //    gövdesi yazılırken, yani bu filtre zinciri TAMAMEN döndükten SONRA ExecuteAsync içinde
            //    gerçekleşir (ölçüldü: A2A.AspNetCore.dll IL'i — StreamResponse burada enumerate ETMEZ,
            //    JsonRpcStreamedResult.ExecuteAsync AsyncEnumerable.Select ile SSE yazarken enumerate
            //    eder). Yalnızca (1) olsaydı bu ikinci çalışma "using" kapandıktan SONRA gerçekleşir ve
            //    scope'u boş bulurdu.
            //
            // Akışsız yol için (2) fazladan ama ZARARSIZ: JsonRpcResponseResult.ExecuteAsync yalnızca
            // ÖNCEDEN hesaplanmış metni serileştirir, accessor'ı bir daha okumaz.
            using var scope = accessor.SetScope(
                sessionId: null, traceId: null, userQuery: null, customerId: customerId);

            var result = await next(context);
            return result is IResult inner ? new ScopedResult(inner, accessor, customerId) : result;
        }

        private sealed class ScopedResult(IResult inner, IApprovalContextAccessor accessor, string customerId) : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                using var scope = accessor.SetScope(
                    sessionId: null, traceId: null, userQuery: null, customerId: customerId);
                await inner.ExecuteAsync(httpContext);
            }
        }
    }
}
