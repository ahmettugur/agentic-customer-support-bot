// Models/ResponseCritique.cs
// ResponseAgent'ın kullanıcıya nihai yanıtı vermeden ÖNCE yaptığı
// Self-critique'in yapılandırılmış özeti.
// Amaç: ton, bütünlük, kaynak kullanımı ve potansiyel hallucination'ları
// Izleyebilmek; gerektiğinde revizyon ihtiyacını işaretleyebilmek.

namespace CustomerSupportBot.Models;

/// <summary>
/// ResponseAgent'ın kendi draft yanıtına verdiği kalite değerlendirmesi.
/// Trace store'da FinalCritique olarak saklanır; kullanıcıya asla gösterilmez.
/// </summary>
public class ResponseCritique
{
    /// <summary>Yanıt kullanıcının orijinal sorusunu ele alıyor mu?</summary>
    public bool AddressesUserQuery { get; set; } = true;

    /// <summary>
    /// Ton değerlendirmesi — "appropriate", "too_formal", "too_casual",
    /// "impolite", "robotic". Empatik/samimi hedeflenir.
    /// </summary>
    public string Tone { get; set; } = "appropriate";

    /// <summary>Yanıtın bütünlük skoru (0.0-1.0). 1.0 = tam, 0.0 = eksik.</summary>
    public double Completeness { get; set; } = 1.0;

    /// <summary>
    /// Hallucination riski — yanıtta specialist/tool çıktısında olmayan
    /// Bilgi veya uydurma var mı? 0.0 = yok, 1.0 = yüksek risk.
    /// </summary>
    public double HallucinationRisk { get; set; } = 0.0;

    /// <summary>
    /// Kaynak atıfı — hangi specialist/tool çıktısından beslenildi?
    /// (ör. "OrderInquiryAgent.resultNotes, ReasoningService.intent").
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>Tespit edilen sorunlar (ton, eksik bilgi, çelişki vb.).</summary>
    public List<string> IssuesFound { get; set; } = new();

    /// <summary>
    /// True ise yanıt revize edilmeli. Şu an tek-geçişli mimari — revizyon ihtiyacı
    /// Sadece trace'e kaydedilir. Gelecekte iki-geçişli loop için kullanılabilir.
    /// </summary>
    public bool RevisionNeeded { get; set; }

    /// <summary>Revizyon önerisi (yanıt nasıl iyileştirilebilirdi).</summary>
    public string RevisionNotes { get; set; } = "";
}
