# Specialist Reasoning Modelleri

**Dosyalar:**
- `Model/SpecialistReasoning.cs`
- `Model/TaskCompletionStatus.cs`

Specialist agent'ların (OrderAgent, ComplaintAgent, ProductAgent, vb.) **tool çağrısından önce ve sonra** ne düşündüklerini yapılandıran modeller.

---

## Neden specialist reasoning?

Specialist agent'lar yalnızca tool çağırmaz — **akıllı** olmaları beklenir:
1. Tool çağırmadan önce: gerekli parametre var mı? Eksik bilgi varsa kullanıcıya sor.
2. Tool çağırdıktan sonra: sonuç tatmin edici mi? Başka agent'a yönlendirilmeli mi?

Bu iki check'i yapılandırmak için `SpecialistReasoning` tipi kullanılır.

---

## SpecialistReasoning

```csharp
public sealed class SpecialistReasoning
{
    public string AgentName { get; set; }                  // "OrderAgent"
    public PreToolCheck? PreToolCheck { get; set; }
    public double ResultConfidence { get; set; }
    public string ResultNotes { get; set; }
    public PostToolReflection? PostToolReflection { get; set; }
}
```

---

## PreToolCheck

Tool çağırmadan önce yapılan kontrol:

```csharp
public sealed class PreToolCheck
{
    public List<string> RequiredParams { get; set; }   // ["order_id"]
    public List<string> CollectedParams { get; set; }  // ["order_id", "customer_id"]
    public List<string> MissingParams { get; set; }    // []
    public bool CanProceed { get; set; }               // true
    public string Reasoning { get; set; }              // "1 elde, sorgu net"
    public double Confidence { get; set; }             // 0.0-1.0
}
```

### Akış

```
Specialist (örn. OrderAgent) çağrıldı
   ↓
LLM PreToolCheck üretir:
   "order_status_tool için order_id lazım — session'da var mı?"
   ↓
CanProceed = false ise:
   → Kullanıcıya soru sor ("Sipariş numaranızı paylaşır mısınız?")
   → Tool çağrısı yapma
   ↓
CanProceed = true ise:
   → Tool çağır
```

Bu sayede specialist boş parametre ile tool çağırıp hata almaz.

---

## PostToolReflection

Tool çağrısından **sonra** sonucu yorumlar:

```csharp
public sealed class PostToolReflection
{
    public bool TaskComplete { get; set; }
    public string Status { get; set; }                          // String
    public TaskCompletionStatus StatusEnum { get; set; }        // Type-safe karşılık
    public string? HandoffSuggestion { get; set; }              // Alternative agent name
    public string? HandoffReason { get; set; }
    public List<string> MissingContext { get; set; } = new();
    public string Summary { get; set; }
}
```

### Status değerleri

```csharp
public enum TaskCompletionStatus
{
    Done,                // İş tamam
    NeedsFollowUp,       // Kullanıcıdan ek bilgi gerek
    NeedsEscalation,     // İnsan agent'a aktarılmalı
    Failed,              // Tool hata verdi, retry de fayda etmez
    Partial              // Kısmen yapıldı
}
```

| Status | Workflow davranışı |
|---|---|
| `Done` | Yanıtı ResponseAgent'a ver, terminate |
| `NeedsFollowUp` | Kullanıcıya soru sor (Summary'i) |
| `NeedsEscalation` | EscalationSink'e kayıt + insan agent'a route |
| `Failed` | Replan veya kullanıcıya hata mesajı |
| `Partial` | Diğer SubTask'lar çalışsın; sonuç partial olarak işaretlensin |

### HandoffSuggestion (dinamik routing)

Specialist görevi tamamlayamazsa, **başka bir agent öner**ebilir:

```json
"handoffSuggestion": "ComplaintAgent",
"handoffReason": "Kullanıcı iade istiyor ama OrderAgent sadece statüs sorgusu yapar"
```

`AgentTeamCoordinator` bu öneriyi okur, runtime'da yönlendirir. Bu sayede PlanningAgent yanlış agent seçse bile sistem düzeltebilir.

---

## Akış örneği

```
Kullanıcı: "5'i nerede"
PlanningAgent → OrderAgent

OrderAgent.PreToolCheck:
  required = ["order_id"]
  collected = ["order_id"]  ← session.CollectedInfo'dan
  canProceed = true
  ↓
OrderAgent.CallTool(order_status_tool, order_id="5")
  → ToolResult: Success, Status="Kargoda"
  ↓
OrderAgent.PostToolReflection:
  taskComplete = true
  status = "done"
  summary = "Sipariş 5 'Kargoda' durumunda"
  ↓
ResponseAgent kullanıcıya yanıt verir
```

### Handoff örneği

```
Kullanıcı: "5 hâlâ gelmedi, iade istiyorum"
PlanningAgent → OrderAgent  (yanlış routing)

OrderAgent.PreToolCheck:
  required = ["order_id"]
  canProceed = true
  ↓
OrderAgent.CallTool(order_status_tool)
  → "Sipariş kargoda"
  ↓
OrderAgent.PostToolReflection:
  taskComplete = false
  status = "needs_escalation"  veya  "needs_followup"
  handoffSuggestion = "ComplaintAgent"
  handoffReason = "Kullanıcı iade talebi var"
  ↓
AgentTeamCoordinator → ComplaintAgent'a yönlendir
```

---

## Status normalizasyonu

LLM farklı kelime kullanabilir; `SpecialistReasoningParser.NormalizeStatus` standardize eder:

| LLM girdisi | Normalize |
|---|---|
| `done`, `completed`, `success`, `tamam` | `done` |
| `needs_followup`, `followup`, `incomplete` | `needs_followup` |
| `needs_escalation`, `escalate`, `human` | `needs_escalation` |
| `failed`, `error`, `fail` | `failed` |
| `partial`, `partially` | `partial` |

Bu sayede Status string + StatusEnum hep tutarlı kalır.

---

## Bağlantılar

- [Services-Parsers.md](Services-Parsers.md) — `SpecialistReasoningParser` parse mantığı
- [Model-Tools.md](Model-Tools.md) — `ToolResult` tool çıktısı
- [Model-Reasoning.md](Model-Reasoning.md) — `PlanningResult` ile karşılaştır
