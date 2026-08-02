# WorkflowRunner

**Dosya:** `CustomerSupportBot.Adapters.Agents/WorkflowRunner.cs`
**Erişim:** `internal sealed`
**Yaşam döngüsü:** Singleton (DI'da `CustomerSupportTeam` içinde `new` ile kurulur, ayrıca kayıtlı değil)

## Ne işe yarar?

Tek bir (decompose edilmemiş) kullanıcı sorgusu için MAF `GroupChat` workflow'unu uçtan uca çalıştırır: mesaj hazırlığı → workflow execution → HITL onay köprüsü → trace toplama → sonuç temizleme → stream event üretimi. Katmanın **gerçek orkestratörü** budur — `CustomerSupportTeam` (bkz. [CustomerSupportTeam.md](CustomerSupportTeam.md)) yalnızca compound/single query ayrımını yapıp bu sınıfa veya `DecomposedRunner`'a yönlendiren ince bir kabuktur.

## Hangi amaçla kullanılır?

`IAgentTeamPort.RunAsync`/`RunStreamingAsync` çağrıldığında (tek sorgu senaryosu) veya `DecomposedRunner` her bir alt-görevi çalıştırırken (compound query senaryosu — bkz. [DecomposedRunner.md](DecomposedRunner.md)) devreye girer. `CustomerSupportBot.Adapters.Agents/Evaluation/EvaluationRunner.cs` de `IAgentTeamPort` üzerinden dolaylı olarak bunu kullanır.

## Sorumlulukları

- Workflow'a gidecek mesaj listesini kurmak (`BuildWorkflowMessagesAsync`): bağlam, reasoning özeti, ID hint'leri, konuşma geçmişi, replan notu, kullanıcı sorgusu — bu sırayla.
- `AgentTeamFactory.CreateWorkflow()` ile taze bir `Workflow` alıp `InProcessExecution.RunStreamingAsync` ile başlatmak, timeout + dış `CancellationToken`'ı `CreateLinkedTokenSource` ile birleştirmek.
- Workflow event akışını (`RequestInfoEvent`, `WorkflowErrorEvent`, `ExecutorInvokedEvent`, `ExecutorCompletedEvent`, `AgentResponseUpdateEvent`, `WorkflowOutputEvent`) yorumlayıp hem trace'e hem (streaming yolda) `StreamEvent`'lere çevirmek (`ApplyTraceEvent`).
- HITL onay köprüsü: `RequestInfoEvent` içindeki `ToolApprovalRequestContent`'i yakalayıp `ApprovalGateService.RequestApprovalAsync`'i çağırmak, kararı `run.SendResponseAsync` ile workflow'a geri vermek (`HandleRequestInfoEventAsync`).
- `human_handoff_tool` çağrıldığında eskalasyonun kod seviyesinde garanti edilmesi (`EnsureHumanHandoffEscalation`) — LLM'in `postToolReflection.status`'ü yanlış/eksik üretmesine karşı tek yönlü bir düzeltme.
- Timeout/iptal/hata durumlarında kooperatif durdurma (`StopRunGracefullyAsync`) ve trace'in doğru `terminationReason`'la kapanmasını sağlamak.
- Sonuç metnini temizlemek (`WorkflowResponseExtractor.RemoveTerminationMarkers`/`RemoveTechnicalJsonBlocks`) ve gerekiyorsa agent-routing sızıntısını LLM ile yeniden yazmak (`RewriteRoutingMessageAsync`).
- Turun bitişini `TurnFinalizer.FinalizeAsync`'e devretmek (eskalasyon işleme, agent visit çıktıları, episodik bellek, müşteri profili, trace kapatma — bkz. [TurnFinalizer.md](TurnFinalizer.md)).

**Üstlenmediği işler:** Ajan/tool tanımları (`AgentTeamFactory`/`Team/*`), routing kararı (`CustomerSupportChatManager`/`Routing/Routing.cs`), compound query bölme (`DecomposedRunner`), onay kuyruğunun kendisi (`ApprovalGateService`/`IApprovalQueue`).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları (constructor injection):** `AgentTeamFactory`, `TurnFinalizer`, `IContextPipeline`, `IChatClient`, `WorkflowGuardOptions`, `IReasoningTraceStore`, `IPromptRepository`, `ApprovalGateService`, `IUiHintEmitter`, `IApprovalContextAccessor`, `ILoggerFactory` — tamamı `CustomerSupportTeam` constructor'ında kurulur ve buraya geçirilir.

**Kimler çağırır:** `CustomerSupportTeam.RunAsync`/`RunStreamingAsync` (tek sorgu), `DecomposedRunner` (her alt-görev için).

**Ne kullanır:** `Microsoft.Agents.AI.Workflows` (`InProcessExecution`, `StreamingRun`, `TurnToken`, `RequestInfoEvent`), `WorkflowResponseExtractor` (sonuç/planning/reasoning çıkarımı — bkz. [WorkflowResponseExtractor.md](WorkflowResponseExtractor.md)), `ExceptionTranslator` (framework hatalarını domain exception'a çevirme), `IdExtractor` (Domain — sorgudan ID çıkarımı).

## Kullanılma nedeni ve tasarım yaklaşımı

Tek Sorumluluk ilkesi gereği eskiden `CustomerSupportTeam` içinde toplu duran orkestrasyon mantığı buraya, `AgentTeamFactory`'ye (ajan/workflow kurulumu) ve `TurnFinalizer`'a (tur-sonu yan etkileri) bölündü — `CustomerSupportTeam` artık yalnızca compound/single ayrımı yapan bir kompozisyon kökü. `RunAsync` (non-streaming) ve `RunStreamingAsync` neredeyse birebir aynı adımları izler; ortak trace toplama mantığı (`TraceState`, `StartTraceState`, `ApplyTraceEvent`, `FinalizeTraceAsync`'e devir) tek yerde tanımlanarak iki yol arasındaki tutarsızlık riski ortadan kaldırıldı — streaming yol yalnızca `yield` sorumluluğunu üstüne ekler.

HITL onay köprüsü (`HandleRequestInfoEventAsync`) framework'ün native `RequestInfoEvent`/`ApprovalRequiredAIFunction` mekanizmasını kullanır — eskiden onay bekleme tool lambda'sının içinde bloklayan bir `await` idi (süreç restart'ında kaybolurdu), şimdi gerçek, checkpoint'lenebilir bir workflow superstep duraklaması (bkz. proje hafızası `hitl-maf-native-migration`).

`EnsureHumanHandoffEscalation`'ın "tek yönlü garanti" tasarımı bilinçli bir takas: kaçırılan eskalasyonun maliyeti fazladan eskalasyondan yüksek görüldüğü için, `human_handoff_tool` çağrıldıysa LLM'in reflection'ı ne derse desin eskalasyon kaydı açılır (admin panelinde dismiss yolu var, tersi mümkün değil).

**`ResolveExtractedIds` — canlıda gözlemlenen bug ve düzeltmesi:** Eskiden `BuildWorkflowMessagesAsync` entity hint'ini her zaman `IdExtractor.Extract(query)` ile (yalnızca güncel mesajdan, regex + Türkçe bağlam kelimesiyle) hesaplıyordu — `ReasoningService`'in aynı turda zaten `EntityVerifier` ile (query+geçmiş+session+DB birleştirerek) hesapladığı daha doğru sonuçtan (`reasoning.VerifiedEntities`) habersizdi. Sonuç: bağlam kelimesiz kısa takip mesajlarında ("sipariş numaram 1042" → "peki 1043") `IdExtractor` sayıyı `customer_id` sanıp yanlış tool'u (`get_last_order_tool`) öneriyor, DB'de gerçekten var olan siparişi "bulunamadı" olarak yanıtlıyordu. Aynı sınıftan ikinci bir örnek `SubTaskOrchestrator.CreateSubTaskReasoning`'de de vardı (compound query alt-görevleri için) — ikisi de düzeltildi, tek doğruluk kaynağı artık `EntityVerifier`'ın ürettiği `VerifiedEntities`.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetWorkflowDiagram()` | `_factory.CreateWorkflow().ToMermaidString()` — admin panelindeki workflow diyagramı endpoint'i için. |
| `RunAsync(query, conversationHistory, session, reasoning, ct)` | Non-streaming tek sorgu koşusu; nihai temizlenmiş metni döner. |
| `RunStreamingAsync(query, conversationHistory, session, reasoning, ct)` | Streaming tek sorgu koşusu; `StreamEvent` akışı üretir (`Agent`, `UiHint`, `ResponseStart`, `ResponseDelta`, `ResponseComplete`, `Error`). |
| `TraceState` (private) | Tek koşunun trace durumu: `Trace`, `ActiveVisits`, `LastAgentSignature`, `IterationCount`, `Result`, `ResponseStreamStarted`, `ResponseFilter`. |
| `ResponseStreamFilter` (private) | ResponseAgent'ın ham token akışından `"TERMINATE"` işaretini ve sonrasını arındırır; chunk sınırı marker'ı bölerse güvenlik payı tutar. |
| `ApplyTraceEvent(st, evt)` (private) | Bir workflow event'inin trace yan etkilerini uygular, varsa (streaming için) `StreamEvent` listesi döner. |
| `EnsureHumanHandoffEscalation(messages, reasonings)` (private static) | `human_handoff_tool` çağrıldıysa `postToolReflection.status`'ü `needs_escalation`'a zorlar (LLM zaten doğru işaretlediyse dokunmaz). |
| `HandleRequestInfoEventAsync(run, requestInfo, ct)` (private) | HITL onay köprüsü — `ToolApprovalRequestContent`'i `ApprovalGateService.RequestApprovalAsync`'e, kararı `run.SendResponseAsync`'e bağlar. |
| `BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning)` (private) | Workflow'a gidecek `ChatMessage` listesini kurar (bağlam → reasoning hint → entity hint → geçmiş → replan notu → sorgu). |
| `ResolveExtractedIds(query, reasoning)` (internal static) | Entity hint'i için ID kaynağını çözer — `reasoning.VerifiedEntities` mevcutsa (ReasoningService'in query+geçmiş+session+DB'yi birleştirdiği sonuç) onu kullanır, yoksa `IdExtractor.Extract(query)` (yalnızca güncel mesaj, bağlamsız) fallback'ine düşer. Bkz. tasarım notu — bu ayrım gerçek bir üretim bug'ını (bağlam kelimesiz takip mesajlarında yanlış tool seçimi) düzeltmek için eklendi. |
| `RewriteRoutingMessageAsync(routingMessage, originalQuery, ct)` (private) | Yanıt metninde ajan adı sızıntısı varsa LLM ile (`routing-rewrite-*` promptları) yeniden yazar; hata olursa sabit fallback mesajı döner. |
| `StopRunGracefullyAsync(run)` (private) | Timeout/iptal anında `run.CancelRunAsync()` ile kooperatif durdurma dener (best-effort). |

## Bağımlılıklar

Constructor injection: `AgentTeamFactory factory`, `TurnFinalizer finalizer`, `IContextPipeline contextPipeline`, `IChatClient chatClient`, `WorkflowGuardOptions guards`, `IReasoningTraceStore traceStore`, `IPromptRepository prompts`, `ApprovalGateService approvalGate`, `IUiHintEmitter uiHint`, `IApprovalContextAccessor approvalContext`, `ILoggerFactory loggerFactory`.
