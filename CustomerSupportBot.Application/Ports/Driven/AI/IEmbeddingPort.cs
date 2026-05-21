namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// Metin → embedding (float[]) üreten secondary port.
/// </summary>
public interface IEmbeddingPort
{
    /// <summary>Tek metin için embedding üretir.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Toplu embedding (KB ingest gibi pahalı işlemler için).</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>Vektör boyutu — koleksiyon yaratırken kullanılır.</summary>
    int Dimension { get; }

    /// <summary>Geçerli bir API key/endpoint ile çalışıyor mu? (false ise memory devre dışı kabul edilmeli.)</summary>
    bool IsConfigured { get; }
}
