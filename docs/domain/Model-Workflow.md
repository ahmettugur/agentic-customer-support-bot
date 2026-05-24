# Workflow Modelleri (Low-Code DSL)

**Dosya:** `Model/Workflow/WorkflowDefinition.cs`

Bot'un **LLM olmadan** çalıştırabileceği deterministic akışları temsil eder. Admin paneli üzerinden tanımlanır.

---

## Neden workflow var?

LLM esnek ama:
- ❌ Maliyetli (her turn cost)
- ❌ Latency yüksek
- ❌ Non-deterministic (aynı girişe farklı yanıt)

Bazı senaryolar **deterministic** ve **sık**:
- "Şube saatleri nedir?" → sabit yanıt
- "Sipariş takip et: ORD-X" → tek tool çağrısı + yanıt
- "İade politikası" → sabit yanıt

Bu durumlarda LLM gereksiz. Workflow ile **regex + tool + template** → mikrosaniyede yanıt.

---

## WorkflowDefinition

```csharp
public sealed class WorkflowDefinition
{
    public string Id { get; set; }                              // Slug
    public string Name { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }

    public List<string> TriggerKeywords { get; set; } = new();           // Tetikleyici kelimeler
    public Dictionary<string, string> InputPatterns { get; set; } = new();// Regex → variable name
    public List<WorkflowStep> Steps { get; set; } = new();
}
```

### Akış başlatma kuralı

Kullanıcı mesajı `TriggerKeywords`'tan birini içeriyorsa workflow çalışır:

```csharp
TriggerKeywords = ["takip", "kargoda", "nerede"]
"siparişim nerede" → match → workflow çalıştır
```

### InputPatterns

Mesajdan değişken çıkarır:

```csharp
InputPatterns = {
    ["order_id"] = "ORD-\\d+",
    ["customer_id"] = "CUST-\\d+"
}

Mesaj: "ORD-5 nerede"
→ variables = { ["order_id"] = "ORD-5" }
```

---

## WorkflowStep

```csharp
public sealed class WorkflowStep
{
    public WorkflowStepType Type { get; set; }
    public string Label { get; set; }                    // UI debug

    // Step-specific
    public string? Template { get; set; }                // Respond
    public string? Tool { get; set; }                    // Lookup
    public Dictionary<string, string>? Parameters { get; set; }
    public string? StoreAs { get; set; }                 // Lookup çıktısı hangi var'a yazılacak
    public string? Condition { get; set; }               // Branch
    public int SkipNext { get; set; } = 1;               // Branch — kaç adımı atla
    public string? VariableName { get; set; }            // SetVariable
    public string? VariableValue { get; set; }
}

public enum WorkflowStepType { Respond, Lookup, Branch, SetVariable }
```

### Step tipleri

#### 1. Respond
Template'i variables ile render eder, kullanıcıya gönderir.

```csharp
new WorkflowStep {
    Type = Respond,
    Template = "Sipariş {order_id} durumu: {order_status}"
}
```

`{order_id}` ve `{order_status}` variable'lardan substitute edilir.

#### 2. Lookup
Read-only tool çağırır, sonucu variable'a yazar.

```csharp
new WorkflowStep {
    Type = Lookup,
    Tool = "order_status_tool",
    Parameters = { ["order_id"] = "{order_id}" },
    StoreAs = "order_status"
}
```

**Forbidden tools:** `order_placement_tool`, `complaint_registration_tool`, `human_handoff_tool` — workflow asla yazma/yan etki tool'u çağıramaz. `WorkflowExecutor` runtime'da bu liste'yi kontrol eder.

#### 3. Branch
Koşul kontrolü; sağlanmıyorsa sonraki N step atlanır.

```csharp
new WorkflowStep {
    Type = Branch,
    Condition = "order_status == Delivered",
    SkipNext = 2   // sonraki 2 step'i atla
}
```

**Koşul tipleri:**

| Operatör | Anlam |
|---|---|
| `var exists` | Variable null değil |
| `var missing` | Variable null veya boş |
| `var == value` | String eşitlik |
| `var != value` | Eşit değil |

#### 4. SetVariable
Variable'a değer atar (template substitution destekler).

```csharp
new WorkflowStep {
    Type = SetVariable,
    VariableName = "greeting",
    VariableValue = "Merhaba {customer_name}"
}
```

---

## Tam workflow örneği

**Sipariş takibi workflow:**

```json
{
  "id": "siparis-takibi",
  "name": "Sipariş Takibi",
  "version": 1,
  "isActive": true,
  "triggerKeywords": ["takip", "nerede", "kargoda"],
  "inputPatterns": {
    "order_id": "ORD-\\d+"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "order_id var mı?",
      "condition": "order_id missing",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Sipariş durumu",
      "tool": "order_status_tool",
      "parameters": { "order_id": "{order_id}" },
      "storeAs": "status_result"
    },
    {
      "type": "Respond",
      "label": "Yanıt",
      "template": "Sipariş {order_id} durumu: {status_result}"
    }
  ]
}
```

Akış:
1. Branch: `order_id` boşsa kalan tüm step'ler atlanır (SkipNext=99)
2. Lookup: `order_status_tool` çağrılır, sonuç `status_result`'a yazılır
3. Respond: Template render edilip kullanıcıya gönderilir

Eğer Branch geçemezse kullanıcı **hiçbir yanıt almaz** — yukarı LLM'li yola fall-through olur.

---

## WorkflowExecutionResult

Workflow'un çıktısı:

```csharp
public sealed class WorkflowExecutionResult
{
    public string WorkflowId { get; init; }
    public bool Success { get; set; }
    public string? FinalResponse { get; set; }
    public List<WorkflowStepTrace> StepTraces { get; set; } = new();
    public Dictionary<string, string> FinalVariables { get; set; } = new();
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public sealed class WorkflowStepTrace
{
    public string StepId { get; init; }
    public WorkflowStepType Type { get; init; }
    public string? Label { get; init; }
    public bool Skipped { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
}
```

`StepTraces` her step'in execution loglarını içerir → admin panelinde debug.

---

## Slugify

Workflow ID otomatik üretilirken Türkçe karakterler normalize edilir:

```
"Sipariş Takibi" → "siparis-takibi"
"İade Politikası" → "iade-politikasi"
"Ürün Bilgisi" → "urun-bilgisi"
```

Kural: `ç→c`, `ğ→g`, `ı→i`, `ö→o`, `ş→s`, `ü→u`, lowercase, alfanümerik olmayanlar `-`.

`InMemoryWorkflowDefinitionStore.Slugify` ve `PostgresWorkflowDefinitionStore.Slugify` aynı algoritmayı kullanır.

---

## Bağlantılar

- [Application WorkflowExecutor](../application/WorkflowExecutor.md) — execution engine
- [Persistence WorkflowDefinitionStore](../adapters-persistence/PostgresAdapters.md#postgresworkflowdefinitionstore)
