using System.Text;
using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CustomerSupportBot.Application.Services.A2A;

namespace CustomerSupportBot.Api.Extensions;

public static class AuthServicesExtensions
{
    public static IServiceCollection AddAuthenticationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        // Başlangıçta, tüm ortamlarda doğrulanır. Eskiden boş anahtarda JwtBearer'a sabit bir
        // yedek ('x' × 32) veriliyor ve hata ancak ilk login'de (scoped JwtAccessTokenProvider
        // ilk kez oluşturulduğunda) çıkıyordu — o ana kadar API, herkesin bildiği o anahtarla
        // imzalanmış token'ları geçerli sayıyordu.
        if (JwtOptions.ValidateSigningKey(jwtOptions.SigningKey) is { } keyError)
            throw new InvalidOperationException(keyError);

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtAccessTokenProvider, JwtAccessTokenProvider>();
        services.AddScoped<ITokenService, TokenPortService>();
        services.AddScoped<IUserService, Application.Services.Auth.UserService>();
        services.AddScoped<ICustomerAuthService, Application.Services.Auth.CustomerAuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false; // Development için
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // Query string token'ı YALNIZCA header taşıyamayan istemcilerin uçlarında.
                //
                // Tarayıcının EventSource ve WebSocket API'leri Authorization header'ı
                // gönderemez; bu uçlar token'ı ?access_token= ile alır. Eskiden bu kabul TÜM
                // uçlara açıktı — URL'deki token erişim loglarına, proxy kayıtlarına ve tarayıcı
                // geçmişine düştüğü için, header taşıyabilen uçlarda kabul edilmemelidir.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (!AcceptsQueryStringToken(ctx.Request.Path)) return Task.CompletedTask;

                        var token = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Admin", p => p.RequireRole("Admin"));
            options.AddPolicy("Agent", p => p.RequireRole("Agent"));
            options.AddPolicy("AdminOrAgent", p => p.RequireRole("Admin", "Agent"));
            options.AddPolicy("Customer", p => p.RequireRole("Customer"));

            // Oturum uçları (/sessions/*) için AÇIK rol listesi.
            //
            // Burada çıplak RequireAuthorization() KULLANILAMAZ: A2A token'ları da aynı JWT
            // şemasıyla doğrulanır, dolayısıyla "kimliği doğrulanmış" olmak yeterli sayılırsa
            // partner token'ı da bu uçlara girer. Partner token'ında linked_customer_id claim'i
            // yoktur; kapsam "claim yoksa sınırsız" kuralıyla hesaplanırsa partner admin gibi
            // değerlendirilip TÜM müşterilerin oturumlarını okuyabilir. A2A token'larının
            // geçerlilik alanı yalnızca /a2a uçlarıdır (bkz. A2ATokenExchangeService).
            options.AddPolicy("SessionAccess", p => p.RequireRole("Customer", "Admin", "Agent"));

            // ─── A2A (dış sistemlere açılan kanal) ───
            // İki ayrı rol bilinçli: partner token'ı müşteri bağımsız ürün ajanını çağırabilir
            // ve token değişimi yapabilir. Müşteri verisi döndüren ajanları çağıran özne token'ı
            // ise tek bir müşteriye kilitlidir. Böylece "hangi sistem" ile "hangi müşteri"
            // soruları ayrı token'larda taşınır ve müşteri kimliği hiçbir zaman istemcinin
            // değiştirebildiği bir parametre olmaz.
            options.AddPolicy("Partner", p => p.RequireRole(A2ARoles.Partner));
            options.AddPolicy("A2ASubject", p => p.RequireRole(A2ARoles.Subject));
        });

        return services;
    }

    /// <summary>
    /// Token'ı query string'den kabul eden uçlar — tarayıcıda header taşıyamayan istemciler:
    /// müşteri olay akışı (<c>/chat/events/{sid}</c>, EventSource), admin/agent canlı devralma
    /// akışı (<c>…/chat-sessions/{sid}/subscribe</c>, EventSource) ve sesli kanallar
    /// (<c>/chat/realtime*</c>, WebSocket). Yeni bir SSE/WS ucu eklenirse buraya da eklenmeli.
    /// </summary>
    internal static bool AcceptsQueryStringToken(PathString path)
    {
        if (path.StartsWithSegments("/chat/events")
            || path.StartsWithSegments("/chat/realtime")
            || path.StartsWithSegments("/chat/realtime-native"))
            return true;

        var value = path.Value ?? "";
        return value.Contains("/chat-sessions/", StringComparison.Ordinal)
            && value.EndsWith("/subscribe", StringComparison.Ordinal);
    }
}
