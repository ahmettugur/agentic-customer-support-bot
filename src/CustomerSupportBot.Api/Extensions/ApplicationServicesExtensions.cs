using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CustomerSupportBot.Adapters.Agents.DependencyInjection;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.DependencyInjection;

using System.Security.Claims;

using CustomerSupportBot.Application.Services.A2A;

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Extensions;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ─── Hexagonal: Application katmanı servisleri ───
        services.AddApplicationDrivingPorts(configuration);

        // Boş origin listesi eskiden HER ortamda AllowAnyOrigin'e düşüyordu — depodaki varsayılan
        // appsettings.json'da liste boş, yani üretim override'ı unutulduğunda her site API'yi
        // tarayıcıdan çağırabiliyordu. Artık yalnızca Development'ta serbest (Blazor :5288 ↔
        // API :5021 kolaylığı); başka ortamda boş liste hiçbir cross-origin çağırana izin vermez
        // (başlatma uyarısı: Program.cs). Liste options oluşturulurken okunur, eager değil.
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IConfiguration, IHostEnvironment>((options, config, env) =>
            {
                var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
                options.AddDefaultPolicy(p =>
                {
                    if (allowedOrigins.Length > 0)
                        p.WithOrigins(allowedOrigins);
                    else if (env.IsDevelopment())
                        p.AllowAnyOrigin();
                    else
                        p.SetIsOriginAllowed(_ => false);

                    p.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Retry-After");
                });
            });

        // Enum'ları camelCase string olarak serialize et (ör. IssueSeverity.Warn → "warn")
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

        // Rate limiting: "chat" = 20/dk, "general" = personel 300/dk, müşteri/kimliksiz 60/dk (RateLimiting),
        // "auth" = 10/dk (kimlik doğrulama uçları — bkz. aşağıdaki not)
        services.Configure<CustomerSupportBot.Api.Infrastructure.RateLimitingOptions>(
            configuration.GetSection(CustomerSupportBot.Api.Infrastructure.RateLimitingOptions.SectionName));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture);

                // Hangi istemcinin hangi uçta kotayı doldurduğu görünmeden "neden 429?" sorusu cevapsız kalıyordu.
                var http = context.HttpContext;
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting").LogWarning(
                    "[RateLimit] 429 | {Method} {Path} | user={User} role={Role} ip={Ip}",
                    http.Request.Method, http.Request.Path.Value,
                    http.User.FindFirst(ClaimTypes.Name)?.Value ?? "-",
                    http.User.FindFirst(ClaimTypes.Role)?.Value ?? "-",
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return ValueTask.CompletedTask;
            };

            // /auth/* öncesinde SINIRSIZDI: login, customer/login, customer/register, refresh
            // hiçbirinde sınır yoktu. Kimlik bilgisi tahmin etme (credential stuffing/brute
            // force) ve kayıt spam'i tek istemciden ucu bucaksız denenebiliyordu. IP tabanlı —
            // bu uçlarda henüz doğrulanmış bir kimlik yok, partner/subject claim'i A2A'daki
            // gibi burada mevcut değil.
            options.AddPolicy("auth", httpContext =>
            {
                var limit = httpContext.RequestServices
                    .GetRequiredService<IOptions<JwtOptions>>().Value.AuthRateLimitPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, limit),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // "chat" MÜŞTERİ başına bölümlenir, IP başına değil. Korunan şey her turdaki LLM
            // maliyetidir: IP anahtarında aynı NAT/kurumsal çıkış arkasındaki müşteriler tek
            // kotayı paylaşıyor, IP değiştirebilen tek bir hesap ise sınırı dolaşıyordu. Uçlar
            // Customer token'ı istediği için claim her zaman vardır; yoksa (kimliksiz istek,
            // zaten yetkilendirmede reddedilecek) IP'ye düşülür. Önekler iki anahtar alanının
            // çakışmasını önler. Limiter UseAuthentication'dan SONRA çalışır (bkz. Program.cs).
            options.AddPolicy("chat", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.FindFirst("linked_customer_id")?.Value is { Length: > 0 } customerId
                        ? $"customer:{customerId}"
                        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            // "general" — 🐞 eskiden tüm trafik IP başına 60/dk idi. Admin/temsilci paneli sürekli yoklar
            // (rozetler + aktif sekme, canlı sohbette duygu durumu, SLA sayfası); aynı IP'deki (yerelde hepsi
            // 127.0.0.1, kurumda aynı NAT) yönetici, temsilci ve müşteri ekranları tek kotayı paylaşınca paneller
            // sürekli 429 alıyordu. Artık: kimliği doğrulanmış personel KULLANICI başına (daha yüksek kota),
            // müşteri MÜŞTERİ başına ("chat" politikasıyla aynı gerekçe), kimliksiz istek IP başına.
            options.AddPolicy("general", httpContext =>
            {
                var limits = httpContext.RequestServices
                    .GetRequiredService<IOptions<CustomerSupportBot.Api.Infrastructure.RateLimitingOptions>>().Value;
                var user = httpContext.User;
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var (partitionKey, limit) =
                    user.Identity?.IsAuthenticated == true && userId is { Length: > 0 } && (user.IsInRole("Admin") || user.IsInRole("Agent"))
                        ? ($"staff:{userId}", limits.StaffPerMinute)
                    : user.FindFirst("linked_customer_id")?.Value is { Length: > 0 } customerId
                        ? ($"customer:{customerId}", limits.GeneralPerMinute)
                    : ($"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}", limits.GeneralPerMinute);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, limit),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // A2A: bölümleme IP'ye DEĞİL PARTNER'a göre yapılır. Dış sistemler proxy/bulut
            // çıkışı arkasında IP paylaşabilir (bir partnerin trafiği diğerinin sınırını
            // tüketirdi) ya da IP değiştirebilir (sınır fiilen ortadan kalkardı). Kimlik
            // token'dan gelir ve çağıran onu değiştiremez.
            //
            // Özne token'ında partner, kimliğin içindedir; oradan çıkarılır ki bir partner
            // çok sayıda müşteri adına çağrı yaparak servisi tek başına tüketemesin.
            options.AddPolicy("a2a", httpContext =>
            {
                var limits = httpContext.RequestServices
                    .GetRequiredService<IOptions<A2AOptions>>().Value;

                var subjectId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var partitionKey =
                    A2ASubjectIdentity.TryGetPartnerId(subjectId)   // özne token'ı → partner
                    ?? subjectId                                    // partner token'ı → kendisi
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()  // kimliksiz → IP
                    ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"a2a:{partitionKey}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, limits.RequestsPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        // Agents adapter — CustomerSupportTeam + ApprovalGateService
        services.AddAgentsAdapter();

        // Chat orchestrators — istek başına yeni instance (Api'ye özgü SSE/HTTP transport)
        services.AddScoped<ChatEventOrchestrator>();

        // ─── Background Workers (hosting adapter) ───
        services.AddHostedService<SlaGuardianService>();
        // Kişisel veri saklama süresi (KVKK) — DataRetention ayarları.
        services.AddHostedService<DataRetentionService>();
        // Onay sonucu e-postası — Email ayarları (kapalıyken gönderim yok).
        services.AddHostedService<ApprovalEmailNotificationService>();
        // Yapay zekâ çözüm oranı: eskalasyon/devralma görüşmeyi "insan dahil" işaretler.
        services.AddHostedService<HumanInvolvementTrackingService>();
        // Sesli görüşme: kayıt parçalarının dökümü + zaman aşımı süpürmesi (çalan → cevapsız, kesilen → başarısız).
        services.AddHostedService<VoiceCallWorker>();
        // RoutingLoadTrackerService kaldırıldı — load-tracking HumanAgentPortService constructor'ında.

        // KnowledgeBase startup adapter — use-case mantığı Application katmanında; bu sadece startup tetikleyicisi
        services.AddHostedService<KnowledgeBaseStartupService>();

        return services;
    }
}
