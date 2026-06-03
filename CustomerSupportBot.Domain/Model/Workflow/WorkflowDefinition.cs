// Domain/Model/Workflow/WorkflowDefinition.cs
// Low-Code Workflow Designer — declarative iş akışı tanımı.
// Adımlar arasındaki geçiş Next/OnTrue/OnFalse referanslarıyla bir DAG oluşturur;
// WorkflowExecutor bu grafiği traversal ederek yürütür.

namespace CustomerSupportBot.Domain.Model.Workflow;

/// <summary>
/// Bir low-code workflow tanımı. JSON olarak persist edilir, runtime'da
/// WorkflowExecutor tarafından adım adım yorumlanır.
/// </summary>
public class WorkflowDefinition
{
    /// <summary>Slug — URL ve API'lerde stabil ID. Lower-case, kısa çizgi.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Şemada uyumsuzluk yaratacak değişiklikler için artırılır.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pasifse executor reddeder.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Yürütmenin başlayacağı adımın Id'si.
    /// Null ise Steps listesindeki ilk eleman kullanılır.
    /// </summary>
    public string? StartStepId { get; set; }

    /// <summary>
    /// Kullanıcı mesajında bu kelimelerden biri geçerse workflow tetiklenir.
    /// Boşsa sadece manuel tetiklemeli.
    /// </summary>
    public List<string> TriggerKeywords { get; set; } = new();

    /// <summary>
    /// Input'tan değişken çıkarmak için regex desenleri.
    /// Key = değişken adı, Value = .NET regex.
    /// </summary>
    public Dictionary<string, string> InputPatterns { get; set; } = new();

    /// <summary>Adım tanımları — executor DAG olarak traversal eder.</summary>
    public List<WorkflowStep> Steps { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

/// <summary>Bir workflow adımının tipi.</summary>
public enum WorkflowStepType
{
    /// <summary>Şablon mesajı variables ile doldurur ve yanıta ekler.</summary>
    Respond,

    /// <summary>Yan etkisiz bir tool çağırır ve sonucu değişkene yazar.</summary>
    Lookup,

    /// <summary>Koşulu değerlendirir; OnTrue veya OnFalse adımına dallanır.</summary>
    Branch,

    /// <summary>Bir değişkene değer atar (şablon substitution destekler).</summary>
    SetVariable
}

/// <summary>
/// Tek bir workflow adımı. Adımlar arası geçiş Next/OnTrue/OnFalse
/// referanslarıyla eksplisit olarak tanımlanır (positional SkipNext yok).
/// </summary>
public class WorkflowStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public WorkflowStepType Type { get; set; }

    /// <summary>UI ve trace için açıklama (opsiyonel).</summary>
    public string? Label { get; set; }

    // ─── Geçiş bağlantıları ───────────────────────────────────────────────

    /// <summary>
    /// Respond, Lookup ve SetVariable için: bu adım tamamlanınca gidilecek adımın Id'si.
    /// Null ise workflow sona erer.
    /// </summary>
    public string? Next { get; set; }

    /// <summary>Branch için: koşul doğruysa gidilecek adım Id'si. Null = sona er.</summary>
    public string? OnTrue { get; set; }

    /// <summary>Branch için: koşul yanlışsa gidilecek adım Id'si. Null = sona er.</summary>
    public string? OnFalse { get; set; }

    // ─── Respond ─────────────────────────────────────────────────────────

    /// <summary>Şablon mesaj — {varName} placeholder'ları variables'tan substitute edilir.</summary>
    public string? Template { get; set; }

    // ─── Lookup ──────────────────────────────────────────────────────────

    /// <summary>Çağrılacak tool adı. Yan etkili tool'lar izin verilmez.</summary>
    public string? Tool { get; set; }

    /// <summary>Tool parametre key → variable adı ($varName) veya literal değer.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>Tool çıktısı bu prefix ile değişkenlere yazılır (storeAs.message, storeAs.success, storeAs.data).</summary>
    public string? StoreAs { get; set; }

    // ─── Branch ──────────────────────────────────────────────────────────

    /// <summary>"varName == value" / "varName != value" / "varName exists" / "varName missing"</summary>
    public string? Condition { get; set; }

    // ─── SetVariable ─────────────────────────────────────────────────────

    public string? VariableName { get; set; }

    /// <summary>Atanacak değer (şablon substitution destekler).</summary>
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

/// <summary>Bir adımın yürütme sonucu — debug ve admin UI için.</summary>
public class WorkflowStepTrace
{
    public string StepId { get; set; } = "";
    public WorkflowStepType Type { get; set; }
    public string? Label { get; set; }
    public bool Skipped { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}
