// Services/Personalization/ICustomerProfileStore.cs

using CustomerSupportBot.Models.Memory;

namespace CustomerSupportBot.Services.Personalization;

public interface ICustomerProfileStore
{
    /// <summary>Var olan profili döndürür, yoksa null.</summary>
    CustomerProfile? Get(string customerId);

    /// <summary>Var olan profili döner, yoksa boş bir tane oluşturup ekler.</summary>
    CustomerProfile GetOrCreate(string customerId);

    /// <summary>Profili upsert eder (tüm alanlar replace).</summary>
    void Upsert(CustomerProfile profile);

    /// <summary>Profili tamamen siler.</summary>
    bool Delete(string customerId);

    /// <summary>Tüm profilleri lastInteraction azalan sırada listeler (admin UI).</summary>
    IReadOnlyList<CustomerProfile> List(int take = 100);

    /// <summary>Toplam profil sayısı.</summary>
    int Count { get; }
}
