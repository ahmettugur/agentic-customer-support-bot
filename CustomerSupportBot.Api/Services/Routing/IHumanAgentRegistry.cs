// Services/Routing/IHumanAgentRegistry.cs
// Smart Routing — İnsan müşteri temsilcisi kayıt arayüzü (InMemoryHumanAgentRegistry
// production'da Postgres ile değiştirilebilir).

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services.Routing;

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

    /// <summary>
    /// Atamadan sonra current load'u +1 yapar ve LastAssignedAt'i günceller.
    /// </summary>
    bool IncrementLoad(string id);

    /// <summary>Eskalasyon kapanınca current load'u -1 yapar (min 0).</summary>
    bool DecrementLoad(string id);
}
