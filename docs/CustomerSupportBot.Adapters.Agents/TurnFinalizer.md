# TurnFinalizer

**Dosya:** `CustomerSupportBot.Adapters.Agents/TurnFinalizer.cs`
**Erişim:** `internal sealed`
**Yaşam döngüsü:** Singleton (`CustomerSupportTeam` içinde `new` ile kurulur)

## Ne işe yarar?

Bir workflow turu **başarıyla** tamamlandığında tetiklenmesi gereken tüm yan etkileri tek bir metotta (`FinalizeAsync`) toplar: bekleyen eskalasyonları işleme, agent visit çıktılarını doldurma, episodik bellek yazımı, müşteri profili güncellemesi ve trace'in kapatılması.

> 💡 **Analiz notu:** Bir maçın bitiminde yapılan işlemler gibi — skortabelasını güncelle, istatistikleri kaydet, maç raporunu yaz. Workflow bitti ama arka planda yapılması gereken işler var.

## Hangi amaçla kullanılır?

`WorkflowRunner.RunAsync`/`RunStreamingAsync`, workflow başarıyla tamamlandığında (timeout/iptal/hata değil) sonuç metni temizlendikten hemen sonra `_finalizer.FinalizeAsync(...)` çağırır.

## Sorumlulukları

- `ApprovalGateService.ProcessPendingEscalations(trace, query, result)` çağırarak `needs_escalation` durumundaki specialist reasoning'leri eskalasyon sink'ine yazdırmak.
- `PopulateAgentVisitOutputs`: trace'teki her `AgentVisit`'in boş kalan `Output` alanını, o ajanın türüne göre (Planning → `trace.Planning` JSON'u, Response → nihai metin, specialist → eşleşen `SpecialistReasoning` JSON'u) 1500 karakterle sınırlı olarak doldurmak — admin panelindeki trace görünümü için.
- `WriteEpisodicMemorySafe`: `ISemanticMemoryWriter` etkinse (`Enabled`), sorgu+yanıtı fire-and-forget (`Task.Run`, hata yutulup loglanır) olarak episodik belleğe yazmak.
- `UpdateCustomerProfileSafeAsync`: `ICustomerProfileService` kayıtlıysa ve session'da `CustomerId` varsa etkileşimi müşteri profiline işlemek (hata yutulup loglanır, yanıtı etkilemez).
- `_traceStore.Complete(...)` ile trace'i `terminationReason` ve `finalResponse` ile kapatmak.

**Üstlenmediği işler:** Timeout/iptal/hata durumlarındaki trace kapatma (bunlar `WorkflowRunner` içinde doğrudan yapılır — `TurnFinalizer` yalnızca **başarılı** turlar için çağrılır), eskalasyon kararının kendisi (`EscalationPolicyService`'e delege edilir).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IReasoningTraceStore`, `ApprovalGateService`, `ILoggerFactory`, `ISemanticMemoryWriter?` (opsiyonel), `ICustomerProfileService?` (opsiyonel) — son ikisi `null` gelirse ilgili özellik sessizce atlanır.

**Kimler çağırır:** Yalnızca `WorkflowRunner.RunAsync`/`RunStreamingAsync` (başarılı tamamlanma dalı).

## Kullanılma nedeni ve tasarım yaklaşımı

Tur-sonu yan etkileri (`ApprovalGateService`, `ISemanticMemoryWriter`, `ICustomerProfileService`, `IReasoningTraceStore`) `WorkflowRunner`'ın kendi akış kontrolü mantığından (event döngüsü, timeout, HITL köprüsü) ayrılarak buraya toplandı — `WorkflowRunner`'ı "workflow'u nasıl çalıştırırım" sorusuna, `TurnFinalizer`'ı "başarılı bir turdan sonra ne yapmam gerekir" sorusuna odaklı tutar. Episodik bellek ve müşteri profili güncellemeleri bilinçli olarak **fire-and-forget/best-effort**: bu yan etkilerin başarısız olması kullanıcının yanıtını etkilememeli, yalnızca loglanmalıdır.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `FinalizeAsync(trace, session, query, result, terminationReason)` | Ana giriş noktası — eskalasyon, agent visit çıktıları, episodik bellek, müşteri profili, trace kapatma sırasıyla çağrılır. |
| `PopulateAgentVisitOutputs(trace, finalResult)` (private static) | Boş `AgentVisit.Output` alanlarını ajan türüne göre doldurur (1500 karakterle kırpılır). |
| `WriteEpisodicMemorySafe(trace, query, response, customerId)` (private) | Fire-and-forget episodik bellek yazımı. `customerId` (`session?.State.AuthenticatedCustomerId`) doldurulur — episode retrieval'ın (bkz. [ContextProviders.md](../CustomerSupportBot.Application/Providers/ContextProviders.md#semanticmemorycontextprovider--episode-retrieval-canlandırıldı)) bu müşteriye ait geçmiş turları bulabilmesi buna dayanır. |
| `UpdateCustomerProfileSafeAsync(session, trace, query, response)` (private) | Best-effort müşteri profili güncellemesi. |

## Bağımlılıklar

Constructor injection: `IReasoningTraceStore traceStore`, `ApprovalGateService approvalGate`, `ILoggerFactory loggerFactory`, `ISemanticMemoryWriter? semanticMemory`, `ICustomerProfileService? profileService`.
