// Models/Memory/CustomerProfile.cs
// Bir müşterinin geçmiş etkileşimlerinden çıkarılmış uzun ömürlü profil.
// Her oturum sonunda heuristik olarak güncellenir; admin tetiklediğinde LLM
// ile özetlenip "PreferredTone / Summary / Notes" alanları zenginleştirilir.
//
// Profilin amacı: kullanıcı CUST-XXX ile yeni bir oturum açtığında, ContextPipeline
// tarafından context'e enjekte edilerek ajanların proaktif/kişiselleştirilmiş
// davranmasını sağlamak (ör. "Bu müşteri kısa ve teknik dil tercih ediyor",
// "Geçmişte iki kez Dell XPS 15 sipariş etti", "Son ortalama puan: 2/5").

namespace CustomerSupportBot.Domain.Model.Memory;

public sealed class CustomerProfile
{
    /// <summary>Müşteri kimliği (ör. "CUST-1990") — primary key.</summary>
    public string CustomerId { get; set; } = "";

    /// <summary>Konuşulan dil (ISO 639-1 — "tr" / "en"). Default = "tr".</summary>
    public string PreferredLanguage { get; set; } = "tr";

    /// <summary>"formal" | "casual" | "concise" | "verbose". LLM consolidate ile dolar.</summary>
    public string PreferredTone { get; set; } = "neutral";

    /// <summary>Niyet → frekans (ör. {"order_status": 5, "complaint": 2}).</summary>
    public Dictionary<string, int> IntentFrequency { get; set; } = new();

    /// <summary>İlgilenilen ürünler (en yenisi başta, max 10).</summary>
    public List<string> ProductInterests { get; set; } = new();

    /// <summary>Son N oturumdan toplanan rating'ler (max 10).</summary>
    public List<int> RecentRatings { get; set; } = new();

    /// <summary>LLM tarafından üretilmiş 1-2 cümlelik kısa profil özeti.</summary>
    public string? Summary { get; set; }

    /// <summary>Admin'in elle eklediği serbest not (override).</summary>
    public string? AdminNote { get; set; }

    /// <summary>Toplam oturum sayısı.</summary>
    public int TotalSessions { get; set; }

    /// <summary>Toplam tur sayısı (kullanıcı mesajı).</summary>
    public int TotalTurns { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConsolidatedAt { get; set; }

    /// <summary>
    /// Profil özetinin context'e enjekte edileceği kısa metin temsili.
    /// Bu metin SemanticMemoryContextProvider gibi yerlerde değil, kendi
    /// CustomerProfileContextProvider'ında format edilir; burası fallback.
    /// </summary>
    public string ToContextLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Summary)) parts.Add(Summary!);
        if (RecentRatings.Count > 0)
        {
            parts.Add($"Son {RecentRatings.Count} oturum ortalama puan: " +
                      $"{RecentRatings.Average():F1}/5");
        }
        if (ProductInterests.Count > 0)
        {
            parts.Add("İlgi alanları: " + string.Join(", ", ProductInterests.Take(3)));
        }
        return string.Join(" | ", parts);
    }
}

