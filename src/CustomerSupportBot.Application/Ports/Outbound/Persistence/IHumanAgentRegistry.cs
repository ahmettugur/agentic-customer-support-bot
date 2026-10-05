using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Müşteri temsilcisi kaydı ve yük yönetimi için secondary port.
///</summary>
public interface IHumanAgentRegistry
{
    /// <summary>Tüm kayıtlı temsilciler (admin UI için).</summary>
    IReadOnlyList<HumanAgent> GetAll();

    /// <summary>Sadece aktif temsilciler (router için).</summary>
    IReadOnlyList<HumanAgent> GetActive();

    HumanAgent? Get(string id);

    HumanAgent Create(HumanAgent agent);

    /// <summary>Var olanı günceller (kısmi update — null olmayan alanlar uygulanır).</summary>
    HumanAgent? Update(string id, HumanAgentInput input);

    /// <summary>Temsilciyi siler — bağlı eskalasyonlar dokunulmaz.</summary>
    bool Delete(string id);

    /// <summary>Atamadan sonra current load'u +1 yapar ve LastAssignedAt'i günceller.</summary>
    bool IncrementLoad(string id);

    /// <summary>Eskalasyon kapanınca current load'u -1 yapar (min 0).</summary>
    bool DecrementLoad(string id);

    /// <summary>
    /// Temsilcinin seçtiği durumu yazar ve kalp atışı sayar. <c>PresenceChangedAt</c> yalnızca durum
    /// değişince güncellenir. Yalnızca durum alanlarına dokunur (yük sayacına değil).
    /// </summary>
    bool SetPresence(string id, AgentPresence presence, DateTime nowUtc);

    /// <summary>Kalp atışı: yalnızca <c>LastSeenAt</c>.</summary>
    bool TouchPresence(string id, DateTime nowUtc);

    /// <summary>
    /// Auth tablosundaki Agent rolü + LinkedAgentId'si olan kullanıcıları döner.
    /// Registry ile merge edilerek tam temsilci listesi oluşturulur.
    /// InMemory implementasyonu boş liste döner.
    /// </summary>
    Task<IReadOnlyList<HumanAgent>> GetLinkedUsersAsync(CancellationToken ct = default);
}
