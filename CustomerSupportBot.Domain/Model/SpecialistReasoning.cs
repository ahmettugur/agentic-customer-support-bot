// Models/SpecialistReasoning.cs
// Specialist ajanların (Product, Order, Complaint) tool çağrısı öncesi/sonrası
// ürettiği yapılandırılmış reasoning.
// Pre-tool check: parametre validasyonu ve karar gerekçesi.
// Post-tool result: sonucun güven skoru ve notları.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir specialist agent'ın tool çağrısı etrafındaki reasoning'i.
/// Agent bunu mesajının başında/sonunda JSON olarak üretir; trace'e yansır.
/// </summary>
public class SpecialistReasoning
{
    /// <summary>Hangi agent ürettikteyse (ör. "OrderAgent").</summary>
    public string AgentName { get; set; } = "";

    /// <summary>Tool çağrısı öncesi parametre doğrulaması.</summary>
    public PreToolCheck? PreToolCheck { get; set; }

    /// <summary>
    /// Tool sonrası result güveni (0.0-1.0). null ise tool çağrılmadı
    /// (pre-check'te eksik param varsa).
    /// </summary>
    public double? ResultConfidence { get; set; }

    /// <summary>
    /// Sonucun kısa özeti (ör. "1030 found, status: delivered"
    /// Veya "no match for 9999").
    /// </summary>
    public string? ResultNotes { get; set; }

    /// <summary>Tool çalıştıktan sonra agent'ın yaptığı reflection.</summary>
    public PostToolReflection? PostToolReflection { get; set; }
}

/// <summary>
/// Specialist agent'ın tool çalıştıktan sonra yaptığı reflection.
/// Görevin tamamlanıp tamamlanmadığını, olası bir dinamik handoff'u,
/// Eksik bağlamı ve kısa özet içerir. ChatManager bu bilgiye göre
/// Bir sonraki adımı seçer.
/// </summary>
public class PostToolReflection
{
    /// <summary>True: kullanıcının asıl ihtiyacı giderildi, ResponseAgent'a geçilebilir.</summary>
    public bool TaskComplete { get; set; }

    /// <summary>
    /// Özet durum — ChatManager routing için kullanır.
    /// Olası değerler: "done", "needs_followup", "needs_escalation", "failed", "partial".
    /// JSON serialization için string olarak kalır; type-safe erişim için <see cref="StatusEnum"/>.
    /// </summary>
    public string Status { get; set; } = WellKnown.TaskStatuses.Done;

    /// <summary>
    /// <see cref="Status"/>'un type-safe enum karşılığı.
    /// Routing kodunda <c>Status == "needs_escalation"</c>
    /// yerine <c>StatusEnum == TaskCompletionStatus.NeedsEscalation</c> kullanılabilir.
    /// </summary>
    public TaskCompletionStatus StatusEnum => Status switch
    {
        WellKnown.TaskStatuses.Done => TaskCompletionStatus.Done,
        WellKnown.TaskStatuses.NeedsFollowUp => TaskCompletionStatus.NeedsFollowUp,
        WellKnown.TaskStatuses.NeedsEscalation => TaskCompletionStatus.NeedsEscalation,
        WellKnown.TaskStatuses.Failed => TaskCompletionStatus.Failed,
        WellKnown.TaskStatuses.Partial => TaskCompletionStatus.Partial,
        WellKnown.TaskStatuses.PendingApproval => TaskCompletionStatus.PendingApproval,
        _ => TaskCompletionStatus.Done
    };

    /// <summary>
    /// Dinamik handoff önerisi — bir başka specialist ajana devredilmeli mi?
    /// Null veya "ResponseAgent" ise akış normal yönde (ResponseAgent'a) devam eder.
    /// Geçerli değerler: "ProductAgent", "OrderAgent",
    /// "ComplaintAgent", "ResponseAgent".
    /// </summary>
    public string? HandoffSuggestion { get; set; }

    /// <summary>Handoff önerisi için gerekçe (1-2 cümle).</summary>
    public string HandoffReason { get; set; } = "";

    /// <summary>Görev bittiyse eksik olan bağlam veya yapılmayanlar.</summary>
    public List<string> MissingContext { get; set; } = new();

    /// <summary>İşlem sonucunun kısa özeti (kullanıcıya yönelik).</summary>
    public string Summary { get; set; } = "";
}

/// <summary>
/// Tool çağrısı yapılıp yapılamayacağının pre-check sonucu.
/// </summary>
public class PreToolCheck
{
    /// <summary>Tool'un gerektirdiği parametre listesi.</summary>
    public List<string> RequiredParams { get; set; } = new();

    /// <summary>Konuşmadan toplanmış parametreler (sadece isimleri).</summary>
    public List<string> CollectedParams { get; set; } = new();

    /// <summary>Henüz elde edilmemiş parametreler.</summary>
    public List<string> MissingParams { get; set; } = new();

    /// <summary>True ise tool çağrılabilir; false ise kullanıcıdan bilgi istenmeli.</summary>
    public bool CanProceed { get; set; }

    /// <summary>Pre-check kararının kısa gerekçesi.</summary>
    public string Reasoning { get; set; } = "";

    /// <summary>Pre-check güven skoru (0.0-1.0).</summary>
    public double Confidence { get; set; } = 0.5;
}

