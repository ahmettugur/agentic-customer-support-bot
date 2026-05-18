// Models/ReasoningTrace.cs
// Her workflow koşusu için reasoning + agent transitions + tool
// Çağrılarını kaydeden trace modeli. OpenTelemetry yerine lightweight
// In-memory store kullanılır; dashboard ve debug amaçlıdır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir workflow çalışmasının tam trace'i — reasoning, agent transitions,
/// Tool çağrıları, termination reason. Debug/dashboard/analiz için.
/// </summary>
public class ReasoningTrace
{
    /// <summary>Trace tekil kimliği (GUID).</summary>
    public string TraceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Bağlı olduğu oturum.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Kullanıcı sorgusu.</summary>
    public string UserQuery { get; set; } = "";

    /// <summary>Workflow başlangıç zamanı (UTC).</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Workflow bitiş zamanı (UTC). Null ise hâlâ çalışıyor.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Tamamlanma süresi (ms). CompletedAt set olunca hesaplanır.</summary>
    public long? DurationMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : null;

    /// <summary>Global reasoning sonucu (ReasoningService çıktısı).</summary>
    public ReasoningResult? Reasoning { get; set; }

    /// <summary>PlanningAgent'ın yapılandırılmış planı (varsa).</summary>
    public PlanningResult? Planning { get; set; }

    /// <summary>
    /// Specialist agent'ların pre-tool check ve result confidence
    /// Reasoning'leri. Her agent için ayrı bir kayıt.
    /// </summary>
    public List<SpecialistReasoning> SpecialistReasonings { get; set; } = new();

    /// <summary>Ziyaret edilen agent'lar (zaman sıralı).</summary>
    public List<AgentVisit> AgentVisits { get; set; } = new();

    /// <summary>Tool çağrıları (zaman sıralı).</summary>
    public List<ToolInvocation> ToolCalls { get; set; } = new();

    /// <summary>Workflow sonlandırma sebebi.</summary>
    public string? TerminationReason { get; set; }

    /// <summary>Nihai yanıt metni (truncate edilmiş olabilir).</summary>
    public string? FinalResponse { get; set; }

    /// <summary>Toplam iterasyon sayısı (MAF superstep count).</summary>
    public int IterationCount { get; set; }

    /// <summary>Hata varsa mesajı.</summary>
    public string? Error { get; set; }

    /// <summary>Tahmini token kullanımı (cost guard için).</summary>
    public long EstimatedTokens { get; set; }
}

/// <summary>
/// Bir agent'ın workflow içinde bir kez çalıştırılması.
/// </summary>
public class AgentVisit
{
    public string AgentName { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : null;

    /// <summary>Agent'ın ürettiği çıktı (truncate 500 char).</summary>
    public string? Output { get; set; }
}

/// <summary>
/// Bir tool çağrısı — hangi tool, hangi parametrelerle, hangi sonuç.
/// </summary>
public class ToolInvocation
{
    public string ToolName { get; set; } = "";
    public DateTime InvokedAt { get; set; } = DateTime.UtcNow;
    public string? AgentName { get; set; }

    /// <summary>Parametreler (JSON string, truncate 500 char).</summary>
    public string? ParametersSummary { get; set; }

    /// <summary>Sonuç (JSON/string, truncate 500 char).</summary>
    public string? ResultSummary { get; set; }

    /// <summary>Başarılı mı.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Tekrar tespiti için kullanılan imza (name:params hash).</summary>
    public string? Signature { get; set; }
}

