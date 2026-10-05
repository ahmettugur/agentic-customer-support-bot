// Postgres/PrivacyChannels.cs
// Kişisel veri silme — silinen oturumların tüm pod'ların önbelleğinden çıkarılması.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// Önbellekli depolar (puan, oturum modu, sohbet köprüsü, eskalasyon, akıl yürütme izi) veriyi
/// pod başına bellekte tutar. Bir pod silme yaptığında kalıcı kaydı siler, kendi önbelleğinden çıkarır
/// ve oturum kimliklerini bu kanala yayınlar; her depo kanala abone olup diğer pod'larda da çıkarır.
/// Yayın en fazla bir kez teslim edilir — kaçan bir mesajda kalıcı kayıt zaten silinmiştir ve önbellek
/// bir sonraki yeniden yüklemede düzelir.
/// </summary>
internal static class PrivacyChannels
{
    public const string SessionsErased = "csbot:privacy:sessions-erased";

    public static void PublishSessionsErased(IMessageBusPort bus, IReadOnlyCollection<string> sessionIds)
    {
        if (sessionIds.Count > 0) bus.Publish(SessionsErased, JsonSerializer.Serialize(sessionIds));
    }

    public static void SubscribeSessionsErased(IMessageBusPort bus, Action<IReadOnlySet<string>> evict, ILogger logger)
    {
        bus.Subscribe(SessionsErased, json =>
        {
            try
            {
                var ids = JsonSerializer.Deserialize<string[]>(json);
                if (ids is { Length: > 0 }) evict(ids.ToHashSet(StringComparer.Ordinal));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Privacy] Silinen oturum yayını işlenemedi");
            }
        });
    }
}
