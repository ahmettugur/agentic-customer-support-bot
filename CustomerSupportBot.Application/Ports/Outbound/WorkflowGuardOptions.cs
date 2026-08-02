// Application/Services/Workflow/WorkflowGuardOptions.cs
// Workflow seviyesi guard ayarları. appsettings.json'dan bind edilir.

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Workflow için guard parametreleri. appsettings.json "WorkflowGuards" bölümü.
/// </summary>
public class WorkflowGuardOptions
{
    /// <summary>
    /// Tüm workflow için saniye cinsinden timeout.
    /// Bu süre aşılırsa CancellationToken tetiklenir.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Aynı tool + aynı parametre combo'sunun maksimum tekrar sayısı.
    /// Bu eşik aşılırsa workflow sonlandırılır.
    /// </summary>
    public int MaxDuplicateToolCalls { get; set; } = 3;

    /// <summary>
    /// Maksimum workflow iterasyon sayısı (MAF superstep).
    /// </summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>
    /// Aynı specialist agent'a yapılabilecek maksimum dinamik handoff sayısı (ping-pong koruması).
    /// </summary>
    public int MaxHandoffsPerAgent { get; set; } = 2;
}
