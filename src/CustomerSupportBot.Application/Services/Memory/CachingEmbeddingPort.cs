// Application/Services/Memory/CachingEmbeddingPort.cs
// IEmbeddingPort dekoratörü: aynı sorgu metni için embedding'i kısa süre bellekte tutar.
//
// Bu bir YANIT önbelleği değildir. Yanıtlar canlı veriye (stok, kargo/sipariş durumu) ve müşteriye bağlıdır; önbelleğe alınırsa bayat ya da başkasının
// bilgisi dönebilir. Embedding ise metnin saf, deterministik fonksiyonudur: aynı soru → aynı vektör. Bu yüzden güvenle önbelleğe alınır ve her tur
// başına yapılan (semantik bellek + dersler + bilgi tabanı) embedding çağrılarını azaltır.
//
// Anahtar metnin SHA-256'sıdır (düz metin bellekte tutulmaz). Yalnızca tek metinli sorgular önbelleğe girer; toplu (KB ingest) çağrılar geçer.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.AI;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed class CachingEmbeddingPort(IEmbeddingPort inner, int maxEntries, TimeSpan ttl, TimeProvider? clock = null) : IEmbeddingPort
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (float[] Vector, DateTime ExpiresAt, DateTime StoredAt)> _entries = new();

    public int Dimension => inner.Dimension;
    public bool IsConfigured => inner.IsConfigured;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(text)) return await inner.EmbedAsync(text, ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim())));

        if (_entries.TryGetValue(key, out var hit) && hit.ExpiresAt > now) return (float[])hit.Vector.Clone();

        // Başarısız çağrı önbelleğe girmez (istisna yukarı çıkar).
        var vector = await inner.EmbedAsync(text, ct);
        _entries[key] = ((float[])vector.Clone(), now + ttl, now);
        Trim(now);
        return vector;
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        inner.EmbedBatchAsync(texts, ct);

    /// <summary>Süresi dolanları, sonra sınır aşılıyorsa en eski kayıtları atar.</summary>
    private void Trim(DateTime now)
    {
        if (_entries.Count <= maxEntries) return;
        foreach (var (k, v) in _entries.Where(e => e.Value.ExpiresAt <= now).ToList()) _entries.TryRemove(k, out _);
        if (_entries.Count <= maxEntries) return;
        foreach (var (k, _) in _entries.OrderBy(e => e.Value.StoredAt).Take(_entries.Count - maxEntries).ToList()) _entries.TryRemove(k, out _);
    }
}
