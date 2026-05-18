// Models/ApprovalOptions.cs
// HITL config — appsettings.json > "HumanInTheLoop" bölümünden bind edilir.
// Hangi tool'ların onay gerektirdiği ve timeout değeri burada tanımlıdır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// HITL ayarları. Approval gate feature'ı Enabled=false ise bypass edilir —
/// Eski davranış korunur (tool direkt çalışır).
/// </summary>
public class ApprovalOptions
{
    /// <summary>HITL aktif mi? Kapatıldığında tool'lar direkt çalışır.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Onay isteyen tool adlarının listesi (snake_case).
    /// Default: yan etkili iki tool.
    /// </summary>
    public List<string> ToolsRequiringApproval { get; set; } = new()
    {
        WellKnown.ToolNames.OrderPlacement,
        WellKnown.ToolNames.ComplaintRegistration
    };

    /// <summary>Admin karar vermezse kaç saniye sonra auto-reject.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Timeout sonrası default karar: true=approve, false=reject.</summary>
    public bool AutoApproveOnTimeout { get; set; } = false;

    /// <summary>
    /// Escalation sink feature flag. Kapatılırsa needs_escalation status'u
    /// Yalnızca metin olarak kalır (eski davranış).
    /// </summary>
    public bool EscalationEnabled { get; set; } = true;
}

