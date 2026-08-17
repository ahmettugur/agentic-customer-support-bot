namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Semantic memory yazma port'u — adapter'ların episodik bellek yazması için.
/// </summary>
public interface ISemanticMemoryWriter
{
    bool Enabled { get; }

    /// <param name="customerId">
    /// Doğrulanmış müşteri kimliği (varsa). Tag olarak yazılır ve retrieval'ın <b>müşteri
    /// bazında</b> filtrelenebilmesini sağlar — sessionId'ye göre filtrelemek yetmez, aynı
    /// müşterinin farklı oturumlardaki (dolayısıyla farklı sessionId'lerdeki) geçmişini
    /// birbirine bağlayamaz. Anonim turlarda <c>null</c>; o episode yalnızca sessionId ile
    /// bulunabilir kalır.
    /// </param>
    Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
        string finalResponse, string? intent, int? rating, string? customerId = null,
        CancellationToken ct = default);
}
