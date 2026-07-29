# WorkflowPortService

**Dosya:** `Services/Workflow/WorkflowPortService.cs`  
**Implements:** `IWorkflowPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Low-code workflow tanımlarını yönetir. Admin panelinden workflow CRUD işlemlerini ve test çalıştırmasını sağlar. `WorkflowExecutor` LLM çağrısı yapmaz — tamamen deterministiktir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IWorkflowDefinitionStore` | Workflow tanımlarının deposu |
| `WorkflowExecutor` | Deterministik workflow yürütücüsü |

---

## Metodlar

### `GetAll`

```csharp
IReadOnlyList<WorkflowDefinition> GetAll()
```

Tüm workflow tanımlarını döner.

---

### `Get`

```csharp
WorkflowDefinition? Get(string id)
```

Tekil workflow tanımını döner.

---

### `Upsert`

```csharp
WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null)
```

Workflow tanımını ekler veya günceller. `updatedBy` audit alanı için kaydedilir.

---

### `Delete`

```csharp
bool Delete(string id)
```

Workflow tanımını siler. Bulunamazsa `false` döner.

---

### `Test`

```csharp
(WorkflowExecutionResult? Result, string? Error) Test(
    string id,
    string input,
    Dictionary<string, string>? variables = null)
```

Belirtilen workflow'u test input'u ile çalıştırır.

**Akış:**
```
1. IWorkflowDefinitionStore.Get(id) → bulunamazsa Error: "Workflow not found."
2. WorkflowExecutor.Execute(def, input, variables)
3. (Result, null) döner
```

---

## WorkflowDefinition modeli

```csharp
public class WorkflowDefinition
{
    string Id;                                  // Slug — URL ve API'lerde stabil ID
    string Name;
    string Description;
    int Version;
    bool IsActive;
    string? StartStepId;                        // Graf başlangıç node'u (null → Steps[0])
    List<string> TriggerKeywords;                // Persist edilir; chat pipeline'ında henüz kullanılmaz
    Dictionary<string, string> InputPatterns;    // varName → regex pattern
    List<WorkflowStep> Steps;
    DateTime CreatedAt;
    DateTime UpdatedAt;
    string? UpdatedBy;
}
```

## WorkflowStep türleri

| Tür | Açıklama |
|-----|---------|
| `Respond` | `{varName}` placeholder'lı şablon metin üretir |
| `Lookup` | Tool çağrısı yapar, sonucu variable olarak saklar |
| `Branch` | Koşulu değerlendirip `OnTrue` veya `OnFalse` node ID'sine dallanır |
| `SetVariable` | Değişken set eder |

---

## WorkflowExecutor detayı

`WorkflowExecutor` LLM çağrısı yapmaz; tamamen deterministik çalışır. Yan etkili tool'ları (order_placement, complaint_registration, human_handoff) güvenlik gerekçesiyle bloke eder.

Desteklenen Lookup tool'ları:
- `product_inquiry_tool`
- `order_status_tool`
- `get_last_order_tool`
- `get_all_orders_tool`

Detaylı bilgi için bkz. [WorkflowExecutor.md](WorkflowExecutor.md).

---

## API endpoint'leri

```http
GET    /workflows             → GetAll
GET    /workflows/{id}        → Get
POST   /workflows             → Upsert (yeni kayıt)
PUT    /workflows/{id}        → Upsert (id ile günceller)
DELETE /workflows/{id}        → Delete
POST   /workflows/{id}/test   → Test
```

Tüm endpoint'ler `Admin` yetkilendirme politikası altında kayıtlıdır (bkz. `Program.cs` — `adminScope.MapWorkflowEndpoints()`).
