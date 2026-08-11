// Application/Services/Sla/SlaOptions.cs
// SLA / Response Time Guardian config.
// HITL onay kuyruğunda veya açık eskalasyonlarda uzun süre bekleyen kayıtlar
// için uyarı + ihlal eşikleri. Guardian periyodik tarayıp aksiyon alır.

namespace CustomerSupportBot.Application.Services.Sla;

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
    /// <summary>Bu süreyi aşan onaylar için "warn" eventi yayınlanır — admin panelinde bir uyarı rozeti.</summary>
    public int WarnAfterSeconds { get; set; } = 20;

    /// <summary>
    /// Bu süreyi aşan onaylar SLA ihlali sayılır ve (varsayılan olarak) yalnızca bir
    /// <c>breach</c> event'i yayınlanır — <see cref="OnBreach"/>'e bakınız.
    /// </summary>
    public int BreachAfterSeconds { get; set; } = 60;

    /// <summary>
    /// Breach sonrası aksiyon. Varsayılan <c>None</c> — sadece event yayınlanır, admin panelinde
    /// "uzun süredir bekliyor" rozeti olarak görünür, karar verilmez.
    ///
    /// <para>
    /// <b>Neden <c>AutoReject</c> DEĞİL:</b> HITL bloklamayan modele taşınmadan önce (o zamanki
    /// tasarımda) bu değer <c>AutoReject</c> idi ve o dönem <c>ApprovalOptions.TimeoutSeconds</c>
    /// (o zamanki tool çağrısını AwaitDecisionAsync ile bekleten, 60sn) ile aynı tutulması gereken,
    /// ikinci/yedek bir uygulama katmanıydı. Tool çağrıları artık admin kararını beklemiyor
    /// (bkz. <c>ApprovalGateService.ExecuteWithApprovalGateAsync</c>) — kayıt, admin karar verene
    /// ya da <c>ApprovalOptions.StalePendingHours</c> (varsayılan 72 saat) aşılana kadar kuyrukta
    /// kalmalı. Tasarım değişirken bu alan güncellenmeyi unutulmuştu: <c>BreachAfterSeconds=60</c> +
    /// <c>OnBreach=AutoReject</c> kombinasyonu, her bekleyen onayı admin bakmasa bile 60. saniyede
    /// sessizce reddediyordu — bloklamayan modelin "admin ne zaman bakarsa baksın" amacını fiilen
    /// geçersiz kılıyordu. <c>AutoReject</c>/<c>AutoApprove</c> hâlâ bir seçenek olarak duruyor
    /// (ör. çok agresif bir operasyon politikası isteniyorsa) ama artık varsayılan değil ve
    /// kasıtlı bir operatör kararı gerektirir.
    /// </para>
    /// </summary>
    public SlaBreachAction OnBreach { get; set; } = SlaBreachAction.None;
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
