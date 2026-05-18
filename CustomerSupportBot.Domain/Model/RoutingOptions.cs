// Models/RoutingOptions.cs
// Smart Routing & Skills-Based Escalation — appsettings.json "Routing" section
// üzerinden okunan konfigürasyon. Intent → skill tag mapping ve routing davranışı.

namespace CustomerSupportBot.Domain.Model;

/// <summary>SkillsBasedRouter için konfigürasyon.</summary>
public class RoutingOptions
{
    /// <summary>Smart routing açık mı? Kapalıysa router no-op döner.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Intent → required skill tags mapping.
    /// Reasoning trace'inde tespit edilen intent'e göre eklenecek skill etiketleri.
    /// </summary>
    public Dictionary<string, List<string>> IntentSkillMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Müşteri profili anahtar kelimeleri → skill tag mapping.
    /// Örn: "VIP" → "vip" (admin notu içeriyorsa).
    /// </summary>
    public Dictionary<string, string> ProfileKeywordSkillMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// LoadBalancing açıksa: aynı match skoruna sahip temsilciler arasında
    /// CurrentLoad düşük olan tercih edilir.
    /// </summary>
    public bool LoadBalancingEnabled { get; set; } = true;

    /// <summary>Skor hesabında dil eşleşmesinin ağırlığı (0..1).</summary>
    public double LanguageWeight { get; set; } = 0.2;

    /// <summary>
    /// Skor eşiği — bu değerin altındaki match'ler "best match yok" sayılır
    /// ve eskalasyon yine kaydedilir ama SuggestedAgentId boş kalır.
    /// </summary>
    public double MinMatchScore { get; set; } = 0.1;

    /// <summary>Seed temsilciler (ilk başlatmada InMemoryHumanAgentRegistry'e eklenir).</summary>
    public List<HumanAgent> SeedAgents { get; set; } = new();
}

