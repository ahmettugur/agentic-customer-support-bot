using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Panelden yönetilen bilgi tabanı makalelerinin kalıcılığı için secondary port.
/// Vector store türetilmiş indekstir; kayıt otoritesi burasıdır.
/// </summary>
public interface IKnowledgeArticleStore
{
    Task<IReadOnlyList<KnowledgeArticle>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Yalnızca yayında olanlar — ingest ve indeksleme bunu kullanır.</summary>
    Task<IReadOnlyList<KnowledgeArticle>> GetPublishedAsync(CancellationToken ct = default);

    Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default);

    Task UpsertAsync(KnowledgeArticle article, CancellationToken ct = default);

    /// <summary>Kayıt yoksa false döner (çağıran 404 üretebilsin diye).</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
