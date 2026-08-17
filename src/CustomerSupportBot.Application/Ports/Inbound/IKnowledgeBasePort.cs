using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>Makale yazma sonucu — kalıcılık başarılı olsa da indeksleme ayrı başarısız olabilir.</summary>
/// <param name="Article">Kaydedilmiş hali (chunk sayısı güncellenmiş).</param>
/// <param name="Indexed">Vector store'a yazıldı mı.</param>
/// <param name="Warning">Indexed=false ise nedeni; panelde gösterilir.</param>
public sealed record KnowledgeArticleSaveResult(KnowledgeArticle Article, bool Indexed, string? Warning);

/// <summary>
/// Bilgi tabanı makalelerinin panelden yönetimi için primary (driving) port.
/// Kaydetme/silme işlemleri vector indeksini de senkron tutar — admin'in
/// ayrıca "yeniden ingest et" demesi gerekmez.
/// </summary>
public interface IKnowledgeBasePort
{
    Task<IReadOnlyList<KnowledgeArticle>> ListAsync(CancellationToken ct = default);

    Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Yeni makale oluşturur ve (yayındaysa) indeksler.</summary>
    Task<KnowledgeArticleSaveResult> CreateAsync(
        string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default);

    /// <summary>Mevcut makaleyi günceller ve indeksi tazeler. Kayıt yoksa null.</summary>
    Task<KnowledgeArticleSaveResult?> UpdateAsync(
        string id, string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default);

    /// <summary>Makaleyi ve indeksteki tüm chunk'larını siler. Kayıt yoksa false.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
