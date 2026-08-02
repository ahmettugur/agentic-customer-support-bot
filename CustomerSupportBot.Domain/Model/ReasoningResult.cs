// Models/ReasoningResult.cs
// Genişletilmiş reasoning şeması.
// Eski alanlar (Analysis, Steps, Intent, RequiredInfo, Confidence) korundu.
// Yeni alanlar: Rationale, Assumptions, NextAction, DecisionReason, ConfidenceScore.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Reasoning çıktısı — yapılandırılmış düşünce süreci.
/// Hem global ReasoningService hem per-agent reasoning için ortak şema.
/// </summary>
public class ReasoningResult
{
    // ─── ORİJİNAL ALANLAR (geriye dönük uyumlu) ───

    /// <summary>Kısa analiz — kullanıcı ne istiyor?</summary>
    public string Analysis { get; set; } = "";

    /// <summary>
    /// Çözüm adımları (sıralı, yapılandırılmış).
    /// Her adım yapılandırılmış bir <see cref="ReasoningStep"/>.
    /// LLM eski formatta (string listesi) döndürürse parser otomatik olarak
    /// Description alanına wrap eder; geriye dönük uyumludur.
    /// </summary>
    public List<ReasoningStep> Steps { get; set; } = new();

    /// <summary>
    /// Algılanan niyet (ör. "sipariş_sorgulama", "ürün_bilgisi", "şikayet").
    /// </summary>
    public string Intent { get; set; } = "";

    /// <summary>Eksik/gerekli bilgiler (ör. "müşteri_kimliği", "sipariş_numarası").</summary>
    public List<string> RequiredInfo { get; set; } = new();

    /// <summary>
    /// Güven seviyesi (legacy string: "yüksek/orta/düşük").
    /// Yeni kod <see cref="ConfidenceScore"/> kullanmalı. Geriye dönük uyumluluk için burada.
    /// </summary>
    public string Confidence { get; set; } = "";

    // ─── YENİ ALANLAR ───

    /// <summary>
    /// Niyet ve planın gerekçesi — neden bu yaklaşım seçildi (1-3 cümle).
    /// 1.3 PlanningAgent için önemli.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Reasoning sırasında yapılan varsayımlar (ör. "1001'in aktif müşteri olduğu",
    /// "kullanıcının TC vatandaşı olduğu"). Transparency için kritik.
    /// </summary>
    public List<string> Assumptions { get; set; } = new();

    /// <summary>
    /// Bir sonraki atılacak somut aksiyon (ör. "OrderAgent'e yönlendir",
    /// "kullanıcıdan customer_id iste", "ComplaintAgent'e escalate et").
    /// Ordered_plan'a temel oluşturur.
    /// </summary>
    public string NextAction { get; set; } = "";

    /// <summary>
    /// Karar gerekçesi — bu next_action neden seçildi, alternatifler neydi
    /// Ve neden seçilmedi. "negatif gerekçeler" için.
    /// </summary>
    public string DecisionReason { get; set; } = "";

    /// <summary>
    /// Sayısal güven skoru 0.0 - 1.0.
    /// &lt;0.5: düşük, 0.5-0.75: orta, &gt;0.75: yüksek.
    /// Clarification threshold için kritik.
    /// Legacy <see cref="Confidence"/> string'i bundan türetilir.
    /// </summary>
    public double ConfidenceScore { get; set; } = 0.5;

    // ─── YENİ ALANLAR ───

    /// <summary>
    /// Sanity checker tarafından tespit edilen tutarsızlıklar.
    /// Boş liste = tutarsızlık yok. Severity=Error olan varsa workflow
    /// Buna göre aksiyon alabilir (ör. confidence düşürme, trace warning).
    /// </summary>
    public List<ReasoningIssue> SanityIssues { get; set; } = new();

    /// <summary>
    /// Compound query decomposition — query birden fazla niyete ayrıştığında
    /// Her parça ayrı bir <see cref="SubTask"/> olarak burada listelenir.
    /// Boş veya 1-elemanlı liste = tek görevli sorgu.
    /// </summary>
    public List<SubTask> SubTasks { get; set; } = new();

    // ─── DUYGU ANALİZİ ───

    /// <summary>
    /// LLM tarafından algılanan duygu etiketi: positive, neutral, negative, angry.
    /// Reasoning prompt'undan JSON olarak gelir.
    /// </summary>
    public string Sentiment { get; set; } = WellKnown.Sentiments.Neutral;

    /// <summary>
    /// LLM tarafından verilen duygu skoru (0.0 = çok olumsuz, 1.0 = çok olumlu).
    /// </summary>
    public double SentimentScore { get; set; } = 0.5;

    /// <summary>
    /// <see cref="Services.Reasoning.EntityVerifier"/>'ın query+history+session+DB birleştirerek
    /// ürettiği doğrulanmış entity sonucu. WorkflowRunner'ın kendi başına (yalnızca güncel
    /// query'den, geçmişten habersiz) entity çıkarımı yapmak yerine bunu yeniden kullanması
    /// için buraya taşınır — tek doğruluk kaynağı burasıdır.
    /// </summary>
    public VerifiedEntities? VerifiedEntities { get; set; }

    /// <summary>
    /// <see cref="ConfidenceScore"/>'un type-safe enum karşılığı.
    /// </summary>
    public ConfidenceLevel ConfidenceLevel => ConfidenceScore switch
    {
        >= 0.75 => ConfidenceLevel.High,
        >= 0.5 => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low
    };

    /// <summary>
    /// <see cref="ConfidenceScore"/>'dan string karşılık üretir.
    /// </summary>
    public static string ScoreToString(double score) => score switch
    {
        >= 0.75 => WellKnown.Confidence.High,
        >= 0.5 => WellKnown.Confidence.Medium,
        _ => WellKnown.Confidence.Low
    };

    /// <summary>
    /// Legacy <see cref="Confidence"/> string'inden skor üretir (parse fallback).
    /// </summary>
    public static double StringToScore(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        WellKnown.Confidence.High or WellKnown.Confidence.HighEn => 0.85,
        WellKnown.Confidence.Medium or WellKnown.Confidence.MediumEn => 0.6,
        WellKnown.Confidence.Low or WellKnown.Confidence.LowEn => 0.3,
        _ => 0.5
    };
}

