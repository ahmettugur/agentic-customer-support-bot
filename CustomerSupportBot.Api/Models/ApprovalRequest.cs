// Models/ApprovalRequest.cs
// HITL — Human-in-the-Loop approval gate modelleri.
// Yüksek riskli tool çağrıları (OrderPlacement, ComplaintRegistration) öncesi
// Kullanıcı/admin onayı bekler. TaskCompletionSource ile async wait tarafı
// IApprovalQueue implementasyonunda tutulur; bu model sadece veri taşır.

namespace CustomerSupportBot.Api.Models;

/// <summary>Approval isteğinin yaşam döngüsü.</summary>
public enum ApprovalStatus
{
    Pending,     // Onay bekliyor
    Approved,    // Admin onayladı
    Rejected,    // Admin reddetti
    Expired      // Timeout ile otomatik reddedildi
}

/// <summary>
/// Bir tool çağrısı için oluşturulmuş approval kaydı. Tool lambda'sı bu
/// Nesneyi queue'ya yazar ve decision'ını await eder.
/// </summary>
public class ApprovalRequest
{
    /// <summary>Benzersiz kimlik (admin endpoint'lerinde kullanılır).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Hangi workflow/session bu talebi üretti.</summary>
    public string? SessionId { get; set; }

    /// <summary>Trace ID — admin panelinden bağlam görmek için.</summary>
    public string? TraceId { get; set; }

    /// <summary>Örn. "order_placement_tool".</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Hangi agent bu tool'u çağırıyor (insan okunur).</summary>
    public string? AgentName { get; set; }

    /// <summary>Parametreler — admin'in kararı verirken göreceği veri.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>Kullanıcı sorusu (bağlam için).</summary>
    public string? UserQuery { get; set; }

    /// <summary>Opsiyonel: LLM'in bu tool'u çağırma gerekçesi (preToolCheck.reasoning).</summary>
    public string? Justification { get; set; }

    /// <summary>İsteğin oluşturulma zamanı.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Karar verilme zamanı (null → Pending).</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Mevcut durum.</summary>
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Kararı veren admin kullanıcı adı (opsiyonel).</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Red/expire gerekçesi (kullanıcıya gösterilir).</summary>
    public string? DecisionReason { get; set; }

    /// <summary>Bu request için timeout (saniye). Config'ten gelir.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Admin endpoint'inin request body'si — approve/reject kararını taşır.
/// </summary>
public class ApprovalDecisionInput
{
    /// <summary>true → approved, false → rejected.</summary>
    public bool Approved { get; set; }

    /// <summary>Kararı veren kişi (opsiyonel, "admin" default).</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Red gerekçesi (reddedilirse kullanıcıya döner).</summary>
    public string? Reason { get; set; }
}
