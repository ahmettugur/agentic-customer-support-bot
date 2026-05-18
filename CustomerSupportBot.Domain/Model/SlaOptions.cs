// Models/SlaOptions.cs
// SLA / Response Time Guardian config (#H).
// HITL onay kuyruğunda veya açık eskalasyonlarda uzun süre bekleyen kayıtlar
// için uyarı + ihlal eşikleri. Guardian periyodik tarayıp aksiyon alır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bekleyen onay/eskalasyon kayıtları için SLA politikası.
/// </summary>
public class SlaOptions
{
    public const string SectionName = "Sla";

    /// <summary>SLA Guardian aktif mi?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tarama frekansı (saniye).</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    public ApprovalSlaOptions Approvals { get; set; } = new();
    public EscalationSlaOptions Escalations { get; set; } = new();
}

public class ApprovalSlaOptions
{
    /// <summary>Bu süreyi aşan onaylar için "warn" eventi yayınlanır.</summary>
    public int WarnAfterSeconds { get; set; } = 20;

    /// <summary>
    /// Bu süreyi aşan onaylar SLA ihlali sayılır ve <see cref="OnBreach"/>'e göre
    /// otomatik karar verilir.
    /// </summary>
    public int BreachAfterSeconds { get; set; } = 45;

    /// <summary>
    /// Breach sonrası otomatik aksiyon:
    /// <c>None</c> = sadece event yayınla (mevcut Approval timeout'u devreye girer),
    /// <c>AutoReject</c> = onayı reddet,
    /// <c>AutoApprove</c> = onayı onayla (sadece güvenli senaryolarda kullan).
    /// </summary>
    public SlaBreachAction OnBreach { get; set; } = SlaBreachAction.AutoReject;
}

public class EscalationSlaOptions
{
    /// <summary>Bu süreyi aşan açık eskalasyonlar için "warn" eventi yayınlanır.</summary>
    public int WarnAfterSeconds { get; set; } = 60;

    /// <summary>Bu süreyi aşan eskalasyonlar SLA ihlali sayılır.</summary>
    public int BreachAfterSeconds { get; set; } = 180;

    /// <summary>
    /// Breach sonrası önceliği bir kademe yükselt
    /// (Low → Normal → High → Critical, Critical en yüksek seviyede sabit kalır).
    /// </summary>
    public bool BoostPriorityOnBreach { get; set; } = true;
}

/// <summary>SLA breach sonrası aksiyon.</summary>
public enum SlaBreachAction
{
    None,
    AutoReject,
    AutoApprove
}

/// <summary>SLA Guardian'ın ürettiği breach/warn olayı.</summary>
public class SlaEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>"approval" | "escalation".</summary>
    public string Kind { get; set; } = "";

    /// <summary>"warn" | "breach".</summary>
    public string Severity { get; set; } = "";

    public string TargetId { get; set; } = "";
    public int AgeSeconds { get; set; }
    public string? Action { get; set; }
    public string? Note { get; set; }
}

