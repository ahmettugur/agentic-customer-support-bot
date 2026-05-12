// Models/AgentSession.cs
// Oturum durumu (state) yönetimi modeli.
// Her kullanıcı oturumunun yaşam döngüsünü, toplanan bilgileri ve fazını takip eder.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Ajan oturumunu temsil eder.
/// Konuşma geçmişi + durum bilgisi (state) birlikte yönetilir.
/// </summary>
public class AgentSession
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
    public SessionState State { get; set; } = new();
}

/// <summary>
/// Oturum içinde tutulan durum bilgileri.
/// Ajanlar arası paylaşılan bağlam olarak kullanılır.
/// </summary>
public class SessionState
{
    /// <summary>Tanımlanan müşteri kimlik numarası (ör: "CUST-001").</summary>
    public string? CustomerId { get; set; }

    /// <summary>Mevcut kullanıcı niyeti (ör: "sipariş_sorgulama", "ürün_bilgisi").</summary>
    public string? CurrentIntent { get; set; }

    /// <summary>Konuşma sırasında toplanan bilgiler (key-value).</summary>
    public Dictionary<string, string> CollectedInfo { get; set; } = new();

    /// <summary>Toplam tur sayısı (kullanıcı-asistan mesaj çifti).</summary>
    public int TurnCount { get; set; }

    /// <summary>Uzun konuşmaların LLM tarafından üretilmiş özeti.</summary>
    public string? ConversationSummary { get; set; }

    /// <summary>Konuşma fazı: greeting, inquiry, action, resolution.</summary>
    public string Phase { get; set; } = WellKnown.Phases.Greeting;

    // ─── Duygu Analizi ───

    /// <summary>Mevcut duygu etiketi: positive, neutral, negative, angry.</summary>
    public string Sentiment { get; set; } = WellKnown.Sentiments.Neutral;

    /// <summary>Mevcut duygu skoru (0.0 = çok olumsuz, 1.0 = çok olumlu).</summary>
    public double SentimentScore { get; set; } = 0.5;

    /// <summary>Son N tur için duygu geçmişi (trend analizi için).</summary>
    public List<SentimentEntry> SentimentHistory { get; set; } = new();

    /// <summary>Ardışık negatif tur sayısı (otomatik eskalasyon tetikleyici).</summary>
    public int ConsecutiveNegativeTurns { get; set; }

    // ─── Admin Replan (manuel "yeniden planla" müdahalesi) ───

    /// <summary>
    /// Admin "Yeniden Planla" tetiklediğinde true olur. Müşterinin bir sonraki
    /// mesajında PlanningAgent'a fresh context'le başlama yönergesi iletişir ve
    /// flag temizlenir (one-shot).
    /// </summary>
    public bool ForceReplanNextTurn { get; set; }

    /// <summary>Replan'ı tetikleyen admin (audit/trace için).</summary>
    public string? ReplanRequestedBy { get; set; }

    /// <summary>Replan işaretlenme anları (audit).</summary>
    public DateTime? ReplanRequestedAt { get; set; }

    /// <summary>
    /// Admin'in replan anında PlanningAgent'a iletmek istediği serbest metin not
    /// (ör. "şikayet için yönlendir"). Müşteriye GÖSTERİLMEZ; sadece bot context'i.
    /// One-shot — hint kullanıldığında temizlenir.
    /// </summary>
    public string? ReplanNote { get; set; }
}

/// <summary>Tek bir tur için duygu kaydı.</summary>
public class SentimentEntry
{
    public int Turn { get; set; }
    public string Label { get; set; } = WellKnown.Sentiments.Neutral;
    public double Score { get; set; } = 0.5;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
