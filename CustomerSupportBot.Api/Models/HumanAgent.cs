// Models/HumanAgent.cs
// Smart Routing & Skills-Based Escalation — Bir insan müşteri temsilcisini
// Skill tag'leri, dil yetkinliği ve mevcut yük (capacity) ile temsil eder.
// SkillsBasedRouter eskalasyon talebi için en uygun temsilciyi seçer.

namespace CustomerSupportBot.Api.Models;

/// <summary>İnsan müşteri temsilcisi profili (skills-based routing için).</summary>
public class HumanAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Görünür temsilci adı (UI ve eskalasyon kaydı için).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>İletişim e-postası (notifikasyon için, opsiyonel).</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Yetkinlik etiketleri — küçük harf, normalize edilmiş.
    /// Örn: ["complaint", "refund", "tr", "vip"]
    /// </summary>
    public List<string> Skills { get; set; } = new();

    /// <summary>Konuştuğu diller (ISO kodları): ["tr", "en"]. Boşsa "tr" varsayılır.</summary>
    public List<string> Languages { get; set; } = new();

    /// <summary>Aktif mi? Pasif temsilciler routing'e dahil edilmez.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Eş zamanlı bakabileceği maksimum açık eskalasyon sayısı (1 = tek seferde tek kayıt).</summary>
    public int MaxConcurrentLoad { get; set; } = 5;

    /// <summary>Şu an üzerinde çalıştığı (Open + Acknowledged) eskalasyon sayısı.</summary>
    public int CurrentLoad { get; set; }

    /// <summary>Routing'de tie-break için manuel öncelik (yüksek olan tercih edilir, default 0).</summary>
    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAssignedAt { get; set; }
}

/// <summary>Admin endpoint'i için temsilci create/update input modeli.</summary>
public class HumanAgentInput
{
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public List<string>? Skills { get; set; }
    public List<string>? Languages { get; set; }
    public bool? IsActive { get; set; }
    public int? MaxConcurrentLoad { get; set; }
    public int? Priority { get; set; }
}

/// <summary>Eskalasyon önceliği — routing skor'una etki eder.</summary>
public enum EscalationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>Routing kararı — SkillsBasedRouter çıktısı.</summary>
public class RoutingDecision
{
    public string? SuggestedAgentId { get; set; }
    public string? SuggestedAgentName { get; set; }
    public double MatchScore { get; set; }
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string? Note { get; set; }
}
