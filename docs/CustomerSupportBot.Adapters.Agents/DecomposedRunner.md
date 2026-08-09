# DecomposedRunner

**Dosya:** `CustomerSupportBot.Adapters.Agents/DecomposedRunner.cs`
**Erişim:** `internal sealed`
**Yaşam döngüsü:** Singleton (`CustomerSupportTeam` içinde `new` ile kurulur)

## Ne işe yarar?

Compound query (bileşik sorgu — ör. "1001'i iptal et ve iade başlat") orkestrasyonunu yapar: reasoning aşamasının ürettiği `SubTasks` listesini `SubTaskOrchestrator.Partition` ile yan-etkisiz/yan-etkili gruplara ayırır, her grubu (paralel veya sıralı) `WorkflowRunner` üzerinden çalıştırır ve sonuçları tek bir yanıtta birleştirir.

## Hangi amaçla kullanılır?

`CustomerSupportTeam.RunAsync`/`RunStreamingAsync`, `SubTaskOrchestrator.IsCompoundQuery(reasoning)` `true` döndürdüğünde (ör. reasoning aşaması sorguyu 2+ alt göreve ayırdıysa) bu sınıfa yönlenir; aksi halde doğrudan `WorkflowRunner` kullanılır.

## Sorumlulukları

- Alt görevleri `SubTaskOrchestrator.Partition(reasoning.SubTasks, parallelOptions)` ile paralel/sıralı gruplara bölmek.
- Paralel gruplar için `SemaphoreSlim` ile `ParallelExecutionOptions.MaxDegreeOfParallelism` sınırını uygulayıp `Task.WhenAll` ile eş zamanlı `WorkflowRunner.RunAsync`/`RunStreamingAsync` çağırmak.
- Sıralı gruplar için alt görevleri birbiri ardına çalıştırıp `runningHistory`'yi (her alt görevin sorusu+yanıtı) bir sonrakine taşımak — böylece sonraki alt görev öncekinin bağlamını görür.
- Her alt görevin sonucunu `SubTaskOrchestrator.FormatSubTaskResult` ile etiketleyip `SortedDictionary<int, string>` (sıra numarasına göre) içinde toplamak, sonunda `SubTaskOrchestrator.AggregateSubTaskResults` ile birleştirmek.
- Streaming yolda (`RunDecomposedStreamingAsync`) `Orchestrator`/`SubTask#N` durumlarını ve alt görevlerin `ResponseDelta`'larını (paralel yolda toplanıp `ResponseComplete`'te tek seferde, sıralı yolda anlık) `StreamEvent` olarak yaymak.

**Üstlenmediği işler:** Tek bir alt görevin gerçek workflow koşusu (`WorkflowRunner`'a devredilir), reasoning'in sorguyu alt görevlere bölme kararı (`ReasoningService`/`SubTaskOrchestrator.IsCompoundQuery`).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `WorkflowRunner` (her alt görev bunun üzerinden çalışır), `ParallelExecutionOptions`, `IUiHintEmitter` (paralel dalların topladığı UI ipuçlarını `DrainPending` ile çekmek için — RunAsync streaming değildir, hint'ler kendiliğinden akmaz), `IApprovalContextAccessor` (paralel/sıralı her alt görev başlarken ambient ajan adını `sub.TargetAgent`'a sabitlemek için — tool çağrılarının/UI ipuçlarının doğru ajana etiketlenmesini sağlar).

**Kimler çağırır:** `CustomerSupportTeam.RunAsync`/`RunStreamingAsync` (compound query dalı).

**Ne kullanır:** `CustomerSupportBot.Application.Services.Reasoning.SubTaskOrchestrator` (`Partition`, `FormatSubTaskQuery`, `CreateSubTaskReasoning`, `FormatSubTaskResult`, `AggregateSubTaskResults`), `WorkflowResponseExtractor.ExtractDeltaText`/`StreamTextInChunksAsync`.

## Kullanılma nedeni ve tasarım yaklaşımı

Compound query orkestrasyonu, tek-sorgu workflow koşusundan (`WorkflowRunner`) kavramsal olarak ayrı bir sorumluluk — "kaç alt görev var, hangileri paralel, sonuçlar nasıl birleşir" sorularını cevaplar, workflow'un kendisini nasıl çalıştıracağını bilmez (onu `WorkflowRunner`'a devreder). Bu ayrım, `WorkflowRunner`'ın tek-sorgu davranışını compound-query karmaşasından izole tutar; `DecomposedRunner` da `WorkflowRunner`'ı bir kara kutu olarak kullanır (yalnızca `RunAsync`/`RunStreamingAsync` imzalarını bilir).

Paralel dalda `_approvalContext.SetCurrentAgent(sub.TargetAgent)` her alt-görev task'ının kendi async akışında ayrı ayrı çağrılır — `IApprovalContextAccessor` ambient (AsyncLocal tabanlı) olduğu için, paralel çalışan görevlerin birbirinin ajan etiketini ezmemesi için bu gereklidir.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunDecomposedAsync(query, conversationHistory, session, reasoning, ct)` | Non-streaming compound query koşusu; birleşik nihai metni döner. |
| `RunDecomposedStreamingAsync(query, conversationHistory, session, reasoning, ct)` | Streaming compound query koşusu; `Orchestrator`/`SubTask#N` durum event'leri + birleşik yanıtın `ResponseStart`/`ResponseDelta`/`ResponseComplete` akışını üretir. |

## Bağımlılıklar

Constructor injection: `WorkflowRunner runner`, `ParallelExecutionOptions parallelOptions`, `IUiHintEmitter uiHint`, `IApprovalContextAccessor approvalContext`.
