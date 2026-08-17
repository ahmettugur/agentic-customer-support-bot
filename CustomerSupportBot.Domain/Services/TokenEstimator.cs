// Domain/Services/TokenEstimator.cs
// Prompt büyüklüğü için kaba tahmin — gerçek tokenizer değil.

namespace CustomerSupportBot.Domain.Services;

/// <summary>
/// Bir tura giden prompt'un kaba token büyüklüğünü tahmin eder.
///
/// <para>
/// <b>Bu bir tokenizer değildir ve faturalama için kullanılamaz.</b> Amacı tek bir soruya cevap
/// vermek: <i>prompt büyüklüğü oturum uzadıkça nasıl bir eğri çiziyor?</i> Bu soru bugün
/// cevapsızdı — <c>ReasoningTrace.EstimatedTokens</c> alanı (DB kolonuna kadar) mevcuttu ama
/// hiçbir yerde yazılmıyor, hep 0 kalıyordu. Ölçüm olmadan bağlam optimizasyonlarının işe
/// yarayıp yaramadığı görülemez.
/// </para>
///
/// <para>
/// Gerçek bir tokenizer (tiktoken vb.) bağımlılığı bilerek eklenmedi: trend izlemek için
/// gereken doğruluk düşük, maliyeti ise yeni bir paket ve model-başına sözlük yönetimi.
/// Mutlak değer yanılabilir; <b>turlar arası oran</b> güvenilirdir ve aranan da odur.
/// </para>
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Karakter/token oranı. İngilizce için yaygın kabul ~4'tür; Türkçe sondan eklemeli ve
    /// aksanlı olduğu için token başına daha az karakter düşer, bu yüzden 3 seçildi.
    /// Tahminin sistematik olarak biraz yüksek çıkması tercih edilir — bütçe uyarısı geç
    /// kalmaktansa erken gelsin.
    /// </summary>
    private const int CharsPerToken = 3;

    /// <summary>
    /// Mesaj başına sabit ek yük (rol etiketi, ayraçlar). Sohbet API'lerinin token
    /// muhasebesinde mesaj başına birkaç token'lık bir çerçeve maliyeti vardır.
    /// </summary>
    private const int PerMessageOverhead = 4;

    /// <summary>Tek bir metnin tahmini token sayısı.</summary>
    public static long Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length / CharsPerToken;

    /// <summary>Bir mesaj dizisinin tahmini toplam token sayısı (çerçeve maliyeti dahil).</summary>
    public static long Estimate(IEnumerable<string?> texts)
    {
        long total = 0;
        foreach (var t in texts)
            total += Estimate(t) + PerMessageOverhead;
        return total;
    }
}
