using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Per-customer profil verisi için secondary port.
///</summary>
public interface ICustomerProfileStore
{
    /// <summary>Var olan profili döndürür, yoksa null.</summary>
    CustomerProfile? Get(string customerId);

    /// <summary>Var olan profili döner, yoksa boş bir tane oluşturup ekler.</summary>
    /// <summary>Yoksa oluşturur ve kalıcılaştırır.</summary>
    Task<CustomerProfile> GetOrCreateAsync(string customerId);

    /// <summary>Profili upsert eder (tüm alanlar replace).</summary>
    /// <summary>Yazma bilinçli olarak CancellationToken almaz: istemci bağlantıyı kesse bile cache ile DB tutarlı kalmalı.</summary>
    Task UpsertAsync(CustomerProfile profile);

    /// <summary>Updates only LLM-derived fields on an existing durable profile; never replaces counters or admin notes.</summary>
    Task<CustomerProfile?> UpdateConsolidationAsync(string customerId, string? summary, string? preferredTone,
        IReadOnlyList<InferredTrait> traits, CancellationToken ct = default);

    /// <summary>Profili tamamen siler.</summary>
    Task<bool> DeleteAsync(string customerId);

    /// <summary>Tüm profilleri lastInteraction azalan sırada listeler (admin UI).</summary>
    IReadOnlyList<CustomerProfile> List(int take = 100);

    /// <summary>Toplam profil sayısı.</summary>
    int Count { get; }
}
