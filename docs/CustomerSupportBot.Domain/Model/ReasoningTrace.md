# ReasoningTrace

**Dosya:** `Model/ReasoningTrace.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `AgentVisit`, `ToolInvocation` class'ları (aynı dosyada)

## 1. Ne İşe Yarar

Bir workflow çalışmasının **tam trace kaydı**dır — reasoning sonucu, agent ziyaretleri, tool çağrıları, süre ölçümleri, SelfCritique, hata bilgisi ve termination reason'ı içerir. Debug, dashboard ve analiz amacıyla kullanılır.

## 2. Hangi Amaçla Kullanılır

Her kullanıcı mesajı işlendiğinde `WorkflowTraceEventProcessor` bir `ReasoningTrace` oluşturur. Admin panelindeki "Traces" ekranında workflow'un adım adım ne yaptığı görüntülenir.

> 💡 **Analiz notu:** Bir uçağın kara kutusu (black box) gibi düşün — her şeyi kaydeder: hangi agent çalıştı, ne kadar sürdü, hangi tool çağrıldı, sonuç ne oldu, hata var mı.

## 3. Metotlar / Üyeler

### ReasoningTrace

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `TraceId` | `string` | Benzersiz trace ID (GUID) |
| `SessionId` | `string` | Bağlı oturum |
| `UserQuery` | `string` | Kullanıcı sorusu |
| `StartedAt` | `DateTime` | Başlangıç zamanı |
| `CompletedAt` | `DateTime?` | Bitiş zamanı (null = hâlâ çalışıyor) |
| `DurationMs` | `long?` | **Computed** — toplam süre (ms) |
| `Reasoning` | `ReasoningResult?` | Global reasoning sonucu |
| `Planning` | `PlanningResult?` | Planlama sonucu |
| `SpecialistReasonings` | `List<SpecialistReasoning>` | Her specialist'in reasoning'i |
| `AgentVisits` | `List<AgentVisit>` | Ziyaret edilen agent'lar (zaman sıralı) |
| `ToolCalls` | `List<ToolInvocation>` | Tool çağrıları (zaman sıralı) |
| `TerminationReason` | `string?` | Workflow neden durdu |
| `FinalResponse` | `string?` | Nihai yanıt (truncate) |
| `SelfCritique` | `SelfCritique?` | ResponseAgent kalite notu |
| `IterationCount` | `int` | MAF superstep sayısı |
| `Error` | `string?` | Hata mesajı |
| `EstimatedTokens` | `long` | Tahmini token kullanımı |

### AgentVisit

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `AgentName` | `string` | Agent adı |
| `StartedAt` | `DateTime` | Başlangıç |
| `CompletedAt` | `DateTime?` | Bitiş |
| `DurationMs` | `long?` | **Computed** — süre (ms) |
| `Output` | `string?` | Agent çıktısı (truncate 500 char) |

### ToolInvocation

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `ToolName` | `string` | Tool adı |
| `InvokedAt` | `DateTime` | Çağrı zamanı |
| `AgentName` | `string?` | Çağıran agent |
| `ParametersSummary` | `string?` | Parametreler (truncate) |
| `ResultSummary` | `string?` | Sonuç (truncate) |
| `Success` | `bool` | Başarılı mı |
| `Signature` | `string?` | Tekrar tespiti imzası |

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Reasoning sonucu
- [PlanningResult.md](PlanningResult.md) — Planlama sonucu
- [SpecialistReasoning.md](SpecialistReasoning.md) — Specialist reasoning'ler
- [SelfCritique.md](SelfCritique.md) — ResponseAgent kalite notu
