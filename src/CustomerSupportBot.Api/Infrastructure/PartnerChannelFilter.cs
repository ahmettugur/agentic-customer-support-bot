// Infrastructure/PartnerChannelFilter.cs
// Dış kanal endpoint'lerine girişte "bu partner bu kanalı kullanabilir mi" kontrolü (a2a / mcp).

using CustomerSupportBot.Application.Ports.Outbound.A2A;
using CustomerSupportBot.Application.Services.A2A;

namespace CustomerSupportBot.Api.Infrastructure;

/// <summary>
/// Yetkilendirme (rol) "geçerli bir partner/özne token'ı mı" sorusunu cevaplar; bu filtre "bu partner bu KANALA girebilir
/// mi"yi. Karar yapılandırmadan her istekte okunur (<see cref="IPartnerChannelAuthorizer"/>), token'dan değil:
/// kanal erişimi token süresi beklenmeden kesilebilir ve çalınmış bir token kendi kanalını genişletemez.
///
/// <para>
/// Dışarıya sessiz 403 döner (partner/yapılandırma ayrıntısı sızmaz); gerçek sebep operatör için loglanır.
/// Filtre, endpoint'in diğer filtrelerinden ÖNCE eklenmelidir (en dışta çalışsın).
/// </para>
/// </summary>
public sealed class PartnerChannelFilter(string channel) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var partnerId = A2APartnerIdentity.Resolve(http.User);
        var authorizer = http.RequestServices.GetRequiredService<IPartnerChannelAuthorizer>();

        if (!authorizer.CanUse(partnerId, channel))
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CustomerSupportBot.PartnerChannels")
                .LogWarning("[{Channel}] REDDEDİLDİ · partner={Partner} · sebep=partnerin bu kanal yetkisi yok", channel, partnerId ?? "bilinmiyor");
            return Results.Forbid();
        }

        return await next(context);
    }
}
