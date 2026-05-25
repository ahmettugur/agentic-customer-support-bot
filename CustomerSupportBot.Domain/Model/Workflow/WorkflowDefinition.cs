// Models/Workflow/WorkflowDefinition.cs
// Low-Code Workflow Designer (#14) — Admin'in declarative olarak tanımlayabileceği
// Mini iş akışları. Mevcut multi-agent sistem dokunulmaz; bu workflow'lar admin
// Tarafından tetiklenir veya intent eşleşmesiyle "fast path" olarak çalışır.

namespace CustomerSupportBot.Domain.Model.Workflow;

/// <summary>
/// Bir low-code workflow tanımı. JSON olarak persist edilir, runtime'da
/// `WorkflowExecutor` tarafından adım adım yorumlanır.
/// </summary>
public class WorkflowDefinition
{
    /// <summary>Slug — URL ve API'lerde stabilize ID. Lower-case, kısa çizgi.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Şemada uyumsuzluk yaratacak değişiklikler için artırılır.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pasifse executor reddeder.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Kullanıcı mesajında bu kelimelerden biri geçerse workflow tetiklenir.
    /// Boş ise sadece manuel/admin tetiklemeli olarak kullanılır.
    /// </summary>
    public List<string> TriggerKeywords { get; set; } = new();

    /// <summary>
    /// Input'tan değişken çıkarmak için regex desenleri.
    /// Key = değişken adı, Value = .NET regex (named-group ya da ilk capture group).
    /// Örn: { "orderId": "(\\d{4,})" }
    /// </summary>
    public Dictionary<string, string> InputPatterns { get; set; } = new();

    /// <summary>Sıralı adımlar — executor sırayla yorumlar.</summary>
    public List<WorkflowStep> Steps { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Audit — son düzenleyen admin.</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>Bir workflow adımının tipi.</summary>
public enum WorkflowStepType
{
    /// <summary>Sabit bir mesaj template'ini variables ile substitute eder ve output'a ekler.</summary>
    Respond,

    /// <summary>Mevcut bir tool'u çağırır (product_inquiry / order_status / get_last_order).</summary>
    Lookup,

    /// <summary>Variables üzerinde koşul değerlendirir; false ise sonraki adım(lar)ı atlar.</summary>
    Branch,

    /// <summary>Bir variables'a yeni değer atar (template substitution destekler).</summary>
    SetVariable
}

/// <summary>
/// Tek bir workflow adımı. Tip'e göre hangi alanların kullanılacağı değişir;
/// Validation çalışma zamanında executor içinde yapılır.
/// </summary>
public class WorkflowStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public WorkflowStepType Type { get; set; }

    /// <summary>UI ve trace için açıklama (opsiyonel).</summary>
    public string? Label { get; set; }

    // ─── Respond ───
    /// <summary>Şablon mesaj — `{varName}` placeholder'ları variables'tan substitute edilir.</summary>
    public string? Template { get; set; }

    // ─── Lookup ───
    /// <summary>Çağrılacak tool adı (WellKnown.ToolNames). Yan etkili tool'lar burada İZİN VERİLMEZ.</summary>
    public string? Tool { get; set; }

    /// <summary>Tool parametre key → variable adı veya literal değer (`$varName` ya da düz string).</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>Tool çıktısı bu variable'a yazılır (Data + Message).</summary>
    public string? StoreAs { get; set; }

    // ─── Branch ───
    /// <summary>"variableName == value" / "variableName != value" / "variableName exists".</summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Condition false olduğunda atlanacak adım sayısı (default 1).
    /// </summary>
    public int SkipNext { get; set; } = 1;

    // ─── SetVariable ───
    public string? VariableName { get; set; }

    /// <summary>Atanacak değer (template substitution destekler).</summary>
    public string? VariableValue { get; set; }
}

/// <summary>Bir workflow execution'ının çıktısı.</summary>
public class WorkflowExecutionResult
{
    public string WorkflowId { get; set; } = "";
    public bool Success { get; set; }
    public string? FinalResponse { get; set; }
    public List<WorkflowStepTrace> StepTraces { get; set; } = new();
    public Dictionary<string, string> FinalVariables { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>Bir adımın yürütme sonucu — debug + admin UI için.</summary>
public class WorkflowStepTrace
{
    public string StepId { get; set; } = "";
    public WorkflowStepType Type { get; set; }
    public string? Label { get; set; }
    public bool Skipped { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

