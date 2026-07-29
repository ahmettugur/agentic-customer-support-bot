# Workflow Modelleri (Low-Code DSL)

**Dosya:** `Model/Workflow/WorkflowDefinition.cs`

Bot'un **LLM olmadan** çalıştırabileceği deterministic akışları temsil eder. Admin paneli üzerinden görsel tasarımcıyla oluşturulur.

---

## Neden workflow var?

LLM esnek ama:
- ❌ Maliyetli (her turn cost)
- ❌ Latency yüksek
- ❌ Non-deterministic (aynı girişe farklı yanıt)

Bazı senaryolar **deterministic** ve **sık**:
- "Şube saatleri nedir?" → sabit yanıt
- "Sipariş takip et: 1030" → tek tool çağrısı + yanıt
- "İade politikası" → sabit yanıt

Bu durumlarda LLM gereksiz. Workflow ile **regex + tool + template** → mikrosaniyede yanıt.

---

## WorkflowDefinition

```csharp
public sealed class WorkflowDefinition
{
    public string Id { get; set; }                               // Slug
    public string Name { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }
    public string? StartStepId { get; set; }                     // Graf başlangıç node'u (null → ilk adım)

    public List<string> TriggerKeywords { get; set; } = new();            // Tetikleyici kelimeler
    public Dictionary<string, string> InputPatterns { get; set; } = new();// Regex → variable name
    public List<WorkflowStep> Steps { get; set; } = new();
}
```

### TriggerKeywords — henüz bağlanmamış

`TriggerKeywords` alanı model, admin tasarımcı UI'ı ve persistence katmanında tam olarak tanımlı ve seed verisinde dolduruluyor, ancak **chat pipeline'ında (`ChatPortService`/`CustomerSupportTeam`) hiçbir yerde okunmuyor**. Şu an workflow'lar yalnızca admin panelindeki "Test Et" ekranından (`WorkflowPortService.Test` → `POST /workflows/{id}/test`) manuel olarak çalıştırılabiliyor; kullanıcı mesajıyla otomatik eşleştirme (fast-path) devrede değil.

```csharp
TriggerKeywords = ["takip", "kargoda", "nerede"]
// Amaçlanan davranış: "siparişim nerede" → match → workflow çalıştır
// Gerçek durum: bu eşleştirme henüz implemente edilmedi
```

### StartStepId

Görsel tasarımcı, node'ların y konumuna göre en yukarıdaki node'u başlangıç olarak seçer.
`null` ise `Steps[0]` kullanılır (geriye dönük uyumluluk).

### InputPatterns

Mesajdan değişken çıkarır:

```csharp
InputPatterns = {
    ["orderId"] = "\\d{4,}",
}

Mesaj: "siparis 1042 nerede"
→ variables = { ["orderId"] = "1042" }
```

---

## WorkflowStep

```csharp
public sealed class WorkflowStep
{
    public string Id { get; set; }                 // Unique node ID (graf düğümü)
    public WorkflowStepType Type { get; set; }
    public string Label { get; set; }              // UI debug

    // Graf bağlantıları (positional SkipNext yerine explicit ID'ler)
    public string? Next { get; set; }              // Respond / Lookup / SetVariable → sonraki node
    public string? OnTrue { get; set; }            // Branch → koşul doğruysa git
    public string? OnFalse { get; set; }           // Branch → koşul yanlışsa git

    // Step-specific
    public string? Template { get; set; }          // Respond
    public string? Tool { get; set; }              // Lookup
    public Dictionary<string, string>? Parameters { get; set; }
    public string? StoreAs { get; set; }           // Lookup çıktısı hangi var'a yazılacak
    public string? Condition { get; set; }         // Branch
    public string? VariableName { get; set; }      // SetVariable
    public string? VariableValue { get; set; }
}

public enum WorkflowStepType { Respond, Lookup, Branch, SetVariable }
```

> **Dikkat:** Eski `SkipNext` alanı kaldırıldı. Artık tüm navigasyon explicit node ID'leriyle yapılır.

### Step tipleri

#### 1. Respond
Template'i variables ile render eder, kullanıcıya gönderir. `Next` ile bir sonraki node'a geçer.

```csharp
new WorkflowStep {
    Id = "r1", Type = Respond,
    Template = "Sipariş {orderId} durumu: {orderResult.message}",
    Next = null  // null → workflow sona erer
}
```

#### 2. Lookup
Read-only tool çağırır, sonucu variable'a yazar. `Next` ile devam eder.

```csharp
new WorkflowStep {
    Id = "lk1", Type = Lookup,
    Tool = "order_status_tool",
    Parameters = { ["orderId"] = "$orderId" },
    StoreAs = "orderResult",
    Next = "r1"
}
```

**Forbidden tools:** `order_placement_tool`, `complaint_registration_tool`, `human_handoff_tool` — workflow asla yazma/yan etki tool'u çağıramaz.

#### 3. Branch
Koşulu değerlendirir; `OnTrue` veya `OnFalse` node'una dallanır. Hedef `null` ise workflow o dalda sona erer.

```csharp
new WorkflowStep {
    Id = "br1", Type = Branch,
    Condition = "orderId exists",
    OnTrue  = "lk1",    // orderId varsa lookup'a git
    OnFalse = "r_ask"   // yoksa sor adımına git
}
```

**Koşul tipleri:**

| Operatör | Anlam |
|---|---|
| `var exists` | Variable null değil ve boş değil |
| `var missing` | Variable yok veya boş |
| `var == value` | String eşitlik |
| `var != value` | Eşit değil |

#### 4. SetVariable
Variable'a değer atar (template substitution destekler). `Next` ile devam eder.

```csharp
new WorkflowStep {
    Id = "sv1", Type = SetVariable,
    VariableName  = "greeting",
    VariableValue = "Merhaba {input}",
    Next = "r1"
}
```

---

## Tam workflow örneği

**Sipariş takibi workflow (graf tabanlı):**

```json
{
  "id": "siparis-takibi",
  "name": "Sipariş Takibi",
  "startStepId": "br1",
  "isActive": true,
  "triggerKeywords": ["takip", "nerede", "kargoda"],
  "inputPatterns": { "orderId": "\\d{4,}" },
  "steps": [
    {
      "id": "br1", "type": "Branch",
      "label": "orderId var mı?",
      "condition": "orderId exists",
      "onTrue": "lk1", "onFalse": "r_ask"
    },
    {
      "id": "lk1", "type": "Lookup",
      "label": "Sipariş sorgula",
      "tool": "order_status_tool",
      "parameters": { "orderId": "$orderId" },
      "storeAs": "siparis",
      "next": "r_result"
    },
    {
      "id": "r_result", "type": "Respond",
      "label": "Sonuç",
      "template": "Sipariş {orderId} durumu: {siparis.message}"
    },
    {
      "id": "r_ask", "type": "Respond",
      "label": "Sipariş no iste",
      "template": "Sipariş numaranızı paylaşır mısınız?"
    }
  ]
}
```

Akış:
1. `br1` — `orderId` var mı? Varsa `lk1`'e, yoksa `r_ask`'a git
2. `lk1` — `order_status_tool` çağır, sonucu `siparis.*` variable'larına yaz, `r_result`'a geç
3. `r_result` — yanıtı render et, sonraki node yok → biter
4. `r_ask` — soru sor, sonraki node yok → biter

---

## WorkflowExecutionResult

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

---

## Bağlantılar

- [Application WorkflowExecutor](../application/WorkflowExecutor.md) — execution engine
- [Web Pages-Workflow](../web/Pages-Workflow.md) — görsel tasarımcı (X6.js)
- [Persistence WorkflowDefinitionStore](../adapters-persistence/PostgresAdapters.md#postgresworkflowdefinitionstore)
