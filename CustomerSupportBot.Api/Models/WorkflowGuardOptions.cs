// Models/WorkflowGuardOptions.cs
// Workflow seviyesi guard ayarları. appsettings.json'dan bind edilir.

namespace CustomerSupportBot.Api.Models;

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
    /// Tek bir istek için maksimum tahmini token kullanımı.
    /// Bu eşik aşılırsa workflow sonlandırılır ve kullanıcıya hata dönülür.
    /// </summary>
    public long MaxTokensPerRequest { get; set; } = 30000;

    /// <summary>
    /// Maksimum workflow iterasyon sayısı (MAF superstep).
    /// </summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>
    /// Aynı specialist agent'a yapılabilecek maksimum dinamik handoff sayısı (ping-pong koruması).
    /// </summary>
    public int MaxHandoffsPerAgent { get; set; } = 2;

    /// <summary>
    /// Planning sonucunda intent confidence bu eşiğin altındaysa clarification yoluna sap.
    /// </summary>
    public double PlanConfidenceThreshold { get; set; } = 0.7;

}
