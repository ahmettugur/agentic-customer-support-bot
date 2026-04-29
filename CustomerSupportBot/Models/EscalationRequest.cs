// Models/EscalationRequest.cs
// HITL — Specialist reasoning'de postToolReflection.status = "needs_escalation"
// Geldiğinde bu talep IEscalationSink'e yazılır. Admin panelden bir insan
// Destek temsilcisi çözüme kavuşturur.

namespace CustomerSupportBot.Models;

/// <summary>Eskalasyon talebinin yaşam döngüsü.</summary>
public enum EscalationStatus
{
    Open,        // Açık — bir temsilci bakmalı
    Acknowledged,// Temsilci aldı ama henüz çözmedi
    Resolved,    // Çözüldü
    Dismissed    // Geçersiz bulundu (yanlış eskalasyon)
}

/// <summary>
/// Workflow sonunda postToolReflection'dan türetilen gerçek eskalasyon kaydı.
/// Bir önceki sürümde bu sadece bir metin vaadiydi — şimdi kuyruğa gerçekten
/// Yazılıyor (HITL).
/// </summary>
public class EscalationRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string? AgentName { get; set; }

    /// <summary>Kullanıcının orijinal sorgusu.</summary>
    public string UserQuery { get; set; } = "";

    /// <summary>LLM'in handoffReason'ı (postToolReflection).</summary>
    public string Reason { get; set; } = "";

    /// <summary>Missing context veya ek notlar.</summary>
    public List<string> MissingContext { get; set; } = new();

    /// <summary>LLM'in üretmiş olduğu son yanıt özeti (temsilci bağlam için).</summary>
    public string? ResponseSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public EscalationStatus Status { get; set; } = EscalationStatus.Open;

    /// <summary>Çözümü yapan temsilci.</summary>
    public string? AssignedTo { get; set; }

    /// <summary>Çözüm notu.</summary>
    public string? Resolution { get; set; }
}

/// <summary>
/// Admin endpoint'i için resolve/ack/dismiss input modeli.
/// </summary>
public class EscalationDecisionInput
{
    /// <summary>"acknowledge" | "resolve" | "dismiss"</summary>
    public string Action { get; set; } = "resolve";

    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
}
