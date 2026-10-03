using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace CustomerSupportBot.Api.Extensions;

/// <summary>
/// Ters proxy / load balancer arkasında gerçek istemci adresini geri kazanır.
///
/// <para>
/// IP tabanlı hız sınırları (<c>auth</c>, <c>general</c>) <c>Connection.RemoteIpAddress</c>'e
/// bakar; proxy arkasında bu adres her istek için proxy'nindir — tüm kullanıcılar tek kotayı
/// paylaşır ve tek bir saldırgan herkesi login'den kilitler.
/// </para>
///
/// <para>
/// <b>Opt-in ve yalnızca güvenilir proxy'ler:</b> <c>X-Forwarded-For</c> istemci tarafından da
/// yazılabilir. Başlık yalnızca <c>ForwardedHeaders:KnownProxies</c> /
/// <c>ForwardedHeaders:KnownNetworks</c>'te listelenen karşı uçlardan geliyorsa dikkate alınır;
/// hiçbiri yapılandırılmamışsa middleware hiç eklenmez. (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>
/// bilinçli olarak kullanılmıyor: o yol güvenilir proxy listesini TEMİZLER ve her karşı ucun
/// başlığına güvenir — her istekte farklı değer gönderen istemci IP sınırını dolaşırdı.)
/// </para>
/// </summary>
public static class ForwardedHeadersExtensions
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services)
    {
        // Yapılandırma options oluşturulurken okunur (eager değil) — test ve ortam
        // katmanlarından gelen tüm kaynaklar o anda birleşmiş olur.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var (proxies, networks) = Read(configuration);
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
                foreach (var network in networks) options.KnownIPNetworks.Add(IPNetwork.Parse(network));
            });
        return services;
    }

    /// <summary>
    /// Boru hattının EN BAŞINA eklenmelidir: sonraki her middleware (hız sınırı, HTTPS
    /// yönlendirmesi, loglama) düzeltilmiş istemci adresini ve şemayı görmeli.
    /// </summary>
    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        var (proxies, networks) = Read(app.Configuration);
        if (proxies.Length == 0 && networks.Length == 0) return app;

        // Options'ı şimdi oluştur: hatalı bir adres/CIDR ilk istekte değil, başlatmada patlasın.
        _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>().Value;

        app.UseForwardedHeaders();
        return app;
    }

    private static (string[] Proxies, string[] Networks) Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return (
            section.GetSection("KnownProxies").Get<string[]>() ?? [],
            section.GetSection("KnownNetworks").Get<string[]>() ?? []);
    }
}
