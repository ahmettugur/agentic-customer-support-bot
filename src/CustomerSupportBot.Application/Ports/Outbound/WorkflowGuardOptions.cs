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
    /// Reasoning çağrısına gönderilecek EN FAZLA geçmiş mesaj sayısı (en yeniler).
    ///
    /// <para>
    /// Workflow tarafında geçmiş özetlenip kırpılıyordu ama reasoning aynı korumadan
    /// yararlanmıyor, oturumun TAMAMINI modele gönderiyordu. Uzun oturumlarda bu üç şeyi
    /// birden büyütür: token maliyeti, gecikme ve modelin bağlam sınırını aşma riski —
    /// sınır aşılırsa reasoning fallback'e düşer ve tur sessizce kalitesizleşir.
    /// </para>
    ///
    /// <para>
    /// Reasoning'in işi niyet çıkarımı ve varlık takibidir; her ikisi de konuşmanın YAKIN
    /// geçmişine dayanır. Uzak turların özeti zaten workflow bağlamında taşınır.
    /// </para>
    /// </summary>
    public int ReasoningHistoryMessages { get; set; } = 12;

    /// <summary>
    /// Reasoning çağrısı için saniye cinsinden timeout.
    ///
    /// <para>
    /// <see cref="TimeoutSeconds"/> yalnızca workflow'u kapsar ve reasoning ondan ÖNCE
    /// çalışır — yani asılı kalan bir reasoning çağrısı hiçbir bütçeye tabi değildi.
    /// </para>
    /// </summary>
    public int ReasoningTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// Maksimum workflow iterasyon sayısı (MAF superstep).
    /// </summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>
    /// Aynı specialist agent'a yapılabilecek maksimum dinamik handoff sayısı (ping-pong koruması).
    /// </summary>
    public int MaxHandoffsPerAgent { get; set; } = 2;
}
