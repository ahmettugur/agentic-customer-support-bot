# WorkflowExecutor

**Dosya:** `Services/Workflow/WorkflowExecutor.cs`  
**Tür:** Singleton (doğrudan DI)

## Ne yapar?

Low-code workflow tanımlarını **graf traversal** ile deterministik olarak çalıştırır. LLM çağrısı yapmaz. Regex tabanlı input analizi, değişken atama, koşul dallanması ve read-only tool lookup'larını destekler. Döngü tespiti ve yan etkili tool'ların bloklanması dahildir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `CustomerSupportToolsService` | Tool çağrıları için (sadece read-only'ler) |
| `ILogger<WorkflowExecutor>` | Step hataları |

---

## `Execute`

```csharp
WorkflowExecutionResult Execute(
    WorkflowDefinition definition,
    string userInput,
    Dictionary<string, string>? initialVariables = null)
```

### Başlangıç değişkenleri

```
vars = { "input": userInput }
initialVariables varsa bunlar da eklenir
```

### Input pattern extraction

`WorkflowDefinition.InputPatterns` içindeki her `(varName, regexPattern)` çifti için:
```
Regex.Match(userInput, pattern)
→ Eşleşirse: vars[varName] = match.Groups[1].Value (capture group 1)
→ Eşleşmezse: atla
```

Bozuk regex pattern exception olursa Warning log yazılır, atlanır (hard fail olmaz).

---

## Graf traversal

Eski positional index loop'un yerini **explicit ID bağlantıları** aldı:

```csharp
var stepMap = definition.Steps.ToDictionary(s => s.Id, StringComparer.Ordinal);
var currentId = definition.StartStepId ?? definition.Steps.FirstOrDefault()?.Id;
var visitedSet = new HashSet<string>(StringComparer.Ordinal);
int execCount = 0;

while (currentId is not null)
{
    if (++execCount > MaxStepExecutions) { result.Error = "Max adım limitine ulaşıldı"; break; }
    if (!visitedSet.Add(currentId))      { result.Error = "Döngü tespit edildi"; break; }

    var step = stepMap[currentId];
    var nextId = ExecuteStep(step, vars, result);
    currentId = nextId;
}
```

`MaxStepExecutions = 50` (sonsuz döngü koruması).  
`visitedSet` ile ziyaret edilen her node takip edilir — aynı ID ikinci kez gelirse döngü hatası verilir.

---

## Step türleri

### `Respond`

```
step.Template içindeki {varName} → vars[varName]
```

Output buffer'a ekler. Birden fazla Respond step'i varsa aralarına satır sonu girer.  
Döndürür: `step.Next` (sonraki node ID'si, null ise biter)

---

### `Lookup`

`step.Tool` adıyla tool çağrısı yapar, sonucu variable olarak saklar.

**Yasak tool'lar (ForbiddenTools):**
- `order_placement_tool`
- `order_cancel_tool`
- `return_request_tool`
- `complaint_registration_tool`
- `human_handoff_tool`

Bu tool'lar çağrılırsa hata — HITL gate'ini bypass etmemek için.

**Desteklenen tool'lar:**
| Tool | Parametre |
|------|----------|
| `product_inquiry_tool` | `productName` / `product_name` |
| `product_list_tool` | — |
| `order_status_tool` | `orderId` / `order_id` |
| `get_last_order_tool` | `customerId` / `customer_id` |
| `get_all_orders_tool` | `customerId` / `customer_id` |

**`step.StoreAs` varsa** sonuç şu variable'lara yazılır:
- `{storeAs}.message` → ToolResult.Message
- `{storeAs}.success` → "true" / "false"
- `{storeAs}.data` → JSON serialize edilmiş data
- `{storeAs}` → Message (kısayol)

**Parametre çözümleme:**
- `$varName` → `vars[varName]`
- `{varName}` → template render
- Literal değer → doğrudan kullan

Döndürür: `step.Next`

---

### `Branch`

```
EvaluateCondition(step.Condition, vars)
→ true:  step.OnTrue  node ID'sine git
→ false: step.OnFalse node ID'sine git
```

Her iki hedef de `null` olabilir — o durumda ilgili dal için workflow sona erer.

**Desteklenen koşul ifadeleri:**

| İfade | Açıklama |
|-------|---------|
| `varName exists` | Değer var ve boş değil |
| `varName missing` | Değer yok veya boş |
| `varName == "value"` | Eşitlik |
| `varName != "value"` | Eşitsizlik |

Döndürür: `step.OnTrue` veya `step.OnFalse`

---

### `SetVariable`

```
vars[step.VariableName] = Render(step.VariableValue, vars)
```

Döndürür: `step.Next`

---

## `Render` (statik)

```csharp
public static string Render(string template, IReadOnlyDictionary<string, string> vars)
```

`{varName}` pattern'ini `vars[varName]` ile değiştirir. Bilinmeyen `{varName}` olduğu gibi bırakılır.

---

## `EvaluateCondition` (statik)

```csharp
public static bool EvaluateCondition(string expression, IReadOnlyDictionary<string, string> vars)
```

Boş expression → `true` döner (koşulsuz devam).

---

## `WorkflowExecutionResult`

```csharp
public class WorkflowExecutionResult
{
    string WorkflowId;
    DateTime StartedAt;
    bool Success;
    string FinalResponse;           // Tüm Respond step'lerinin birleşimi
    Dictionary<string, string> FinalVariables;  // Son değişken durumu
    List<WorkflowStepTrace> StepTraces;         // Her adımın çıktısı ve hatası
    string? Error;
    long DurationMs;
}
```

---

## Örnek workflow tanımı

```json
{
  "id": "order-query",
  "name": "Sipariş Sorgula",
  "startStepId": "br1",
  "isActive": true,
  "inputPatterns": { "orderId": "\\b(\\d{4,})\\b" },
  "steps": [
    {
      "id": "br1", "type": "Branch",
      "condition": "orderId exists",
      "onTrue": "lk1", "onFalse": "r_ask"
    },
    {
      "id": "lk1", "type": "Lookup",
      "tool": "order_status_tool",
      "parameters": { "orderId": "$orderId" },
      "storeAs": "orderResult",
      "next": "r_result"
    },
    {
      "id": "r_result", "type": "Respond",
      "template": "Sipariş {orderId} durumu: {orderResult.message}"
    },
    {
      "id": "r_ask", "type": "Respond",
      "template": "Sipariş numaranızı paylaşır mısınız?"
    }
  ]
}
```

---

## WorkflowPortService ile ilişki

`WorkflowExecutor` doğrudan API'ye açılmaz. `WorkflowPortService.Test` metodu test çalıştırması için `WorkflowExecutor.Execute` çağırır. Detay için bkz. [WorkflowPortService.md](WorkflowPortService.md).
