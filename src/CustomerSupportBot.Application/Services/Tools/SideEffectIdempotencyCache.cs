// Application/Services/Tools/SideEffectIdempotencyCache.cs
// Yan etkili tool çağrıları için kısa pencereli mükerrer-çağrı koruması.
//
// Neden gerekli?
//   MaxDuplicateToolCalls guard'ı CustomerSupportChatManager içinde, yani TEK bir
//   workflow koşusunun mesaj geçmişine bakar. Compound query'de her alt görev AYRI
//   bir workflow koşusu olarak (bazen paralel) çalıştığı için o guard mükerrer
//   order_placement_tool / complaint_registration_tool çağrılarını göremez.
//   LLM'in aynı tool'u yeniden çağırması ve istemci tarafı çift gönderim de aynı
//   sonucu doğurur. Bu cache, süreç genelinde son N saniyedeki aynı-parametreli
//   çağrıyı yakalar.
//
// Davranış:
//   Cache isabetinde tool ÇALIŞTIRILMAZ (DB'ye yazılmaz), ancak ilk sonuç sessizce
//   taklit de edilmez — çağırana "bu kaydı az önce oluşturdum" bilgisini içeren
//   ayırt edilebilir bir sonuç döndürmesi için orijinal kayıt geri verilir.
//   Böylece hem mükerrer kayıt engellenir hem meşru tekrar talebi görünür kalır.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

/// <summary>
/// Bir yan etkili tool çağrısının cache'lenmiş sonucu.
/// </summary>
/// <param name="Result">Orijinal çağrının döndürdüğü sonuç.</param>
/// <param name="EntityId">Oluşturulan kaydın kimliği (sipariş/şikayet numarası) — uyarı mesajında kullanılır.</param>
/// <param name="RecordedAt">Kaydın alındığı UTC zamanı.</param>
public sealed record IdempotentCall(ToolResult Result, string? EntityId, DateTimeOffset RecordedAt);

/// <summary>
/// Yan etkili tool'lar için parametre-imzası tabanlı mükerrer çağrı cache'i.
/// Thread-safe; paralel alt görevlerden eş zamanlı çağrılabilir.
/// </summary>
public sealed class SideEffectIdempotencyCache
{
    /// <summary>Varsayılan mükerrer tespit penceresi.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    /// <summary>Bellekte tutulacak maksimum kayıt sayısı; aşılırsa en eski kayıtlar atılır.</summary>
    public const int DefaultMaxEntries = 200;

    private readonly TimeSpan _window;
    private readonly int _maxEntries;
    private readonly TimeProvider _clock;

    private readonly object _gate = new();
    private readonly Dictionary<string, IdempotentCall> _entries = new(StringComparer.Ordinal);

    public SideEffectIdempotencyCache(
        TimeSpan? window = null,
        int maxEntries = DefaultMaxEntries,
        TimeProvider? clock = null)
    {
        _window = window ?? DefaultWindow;
        _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Aynı tool aynı parametrelerle pencere içinde çağrılmış mı? Çağrılmışsa orijinal kayıt döner.
    /// </summary>
    public bool TryGetRecent(
        string toolName,
        IReadOnlyList<object?> parameters,
        out IdempotentCall recent)
    {
        var key = BuildKey(toolName, parameters);
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            PruneExpired(now);

            if (_entries.TryGetValue(key, out var found))
            {
                recent = found;
                return true;
            }
        }

        recent = null!;
        return false;
    }

    /// <summary>
    /// Başarılı bir yan etkili çağrıyı kaydeder. Başarısız sonuçlar cache'lenmez —
    /// hata durumunda tekrar denemenin engellenmesi istenmez.
    /// </summary>
    public void Record(
        string toolName,
        IReadOnlyList<object?> parameters,
        ToolResult result,
        string? entityId)
    {
        if (!result.Success) return;

        var key = BuildKey(toolName, parameters);
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            PruneExpired(now);
            _entries[key] = new IdempotentCall(result, entityId, now);

            if (_entries.Count > _maxEntries)
            {
                // En eski kayıtları at (LRU yerine FIFO — pencere kısa olduğu için yeterli).
                var overflow = _entries.Count - _maxEntries;
                foreach (var stale in _entries
                             .OrderBy(kv => kv.Value.RecordedAt)
                             .Take(overflow)
                             .Select(kv => kv.Key)
                             .ToList())
                {
                    _entries.Remove(stale);
                }
            }
        }
    }

    /// <summary>Test ve oturum sıfırlama için cache'i temizler.</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    // _gate kilidi altında çağrılmalı.
    private void PruneExpired(DateTimeOffset now)
    {
        if (_entries.Count == 0) return;

        List<string>? expired = null;
        foreach (var (key, call) in _entries)
        {
            if (now - call.RecordedAt >= _window)
                (expired ??= []).Add(key);
        }

        if (expired is null) return;
        foreach (var key in expired) _entries.Remove(key);
    }

    private static string BuildKey(string toolName, IReadOnlyList<object?> parameters)
    {
        var sb = new StringBuilder(toolName);
        for (var i = 0; i < parameters.Count; i++)
        {
            sb.Append('');   // unit separator — parametre değerlerinde geçmeyeceği varsayılır
            sb.Append(parameters[i]?.ToString() ?? string.Empty);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
