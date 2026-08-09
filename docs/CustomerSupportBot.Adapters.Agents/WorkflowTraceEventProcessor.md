# WorkflowTraceEventProcessor

## Ne İşe Yarar
Workflow event'lerini (executor invoked/completed, agent response update, workflow output) trace yan etkilerine ve stream event'lerine çeviren sınıftır.

## Hangi Amaçla Kullanılır
[WorkflowRunner](WorkflowRunner.md) bir workflow koşarken üretilen event'leri alır, trace state'i günceller ve reasoning trace store'a kaydeder. #47 refactor'ı ile WorkflowRunner'dan ayrıştırılmıştır.

## Sorumlulukları
- Tek bir workflow koşusunun trace toplama durumunu (`TraceState`) yönetmek.
- `ExecutorInvoked` / `ExecutorCompleted` event'lerini agent visit trace kaydına çevirmek.
- `AgentResponseUpdate` event'lerini stream event'lerine ve tool call trace'lerine çevirmek.
- `WorkflowOutput` event'ini final response trace'ine çevirmek.
- `ResponseStreamFilter` ile ResponseAgent'ın ham token akışından "TERMINATE: reason=..." işaretini ve self-critique JSON bloğunu arındırmak.
- Approval context'i trace'e bağlamak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IReasoningTraceStore`, `IApprovalContextAccessor`.
- **Kullanan sınıf**: [WorkflowRunner](WorkflowRunner.md).
- **Event kaynağı**: `Microsoft.Agents.AI.Workflows` — Workflow event modeli.
- **İlişkili model**: `CustomerSupportBot.Domain.Model` → `ReasoningTrace`, `AgentVisit`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
WorkflowRunner'ın SRP ihlali çözülerek "bir event geldiğinde trace'e ne olur?" sorusunun cevabı bu sınıfa taşınmıştır. `ResponseStreamFilter` chunk sınırları marker'ı bölebileceğinden marker uzunluğu kadar güvenlik payı tutar; yalnızca kesinlikle marker'a ait olmadığı bilinen kısım hemen yayınlanır.

## İç Sınıflar

| Sınıf | Açıklama |
|-------|----------|
| `TraceState` | Tek bir workflow koşusunun trace toplama durumu: Trace nesnesi, aktif agent visit'leri, iterasyon sayısı, sonuç metni, ResponseStreamStarted flag'i. |
| `ResponseStreamFilter` | ResponseAgent'ın ham token akışından "TERMINATE" marker'ını ve sonrasındaki self-critique JSON'unu filtreleyen stateful mekanizma. |

## Bağımlılıklar
- `IReasoningTraceStore` — Trace kaydetme.
- `IApprovalContextAccessor` — Approval bağlamı.
- `Microsoft.Agents.AI.Workflows` — Workflow event tipleri.
