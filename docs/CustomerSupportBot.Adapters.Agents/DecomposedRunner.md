# DecomposedRunner

**Dosya:** `CustomerSupportBot.Adapters.Agents/DecomposedRunner.cs`
**Erişim:** `internal sealed`
**Yaşam döngüsü:** Singleton (`CustomerSupportTeam` içinde `new` ile kurulur)

## Ne işe yarar?

Compound query (bileşik sorgu — ör. "1001'i iptal et ve iade başlat") orkestrasyonunu yapar: reasoning aşamasının ürettiği `SubTasks` listesini `SubTaskOrchestrator.Partition` ile yan-etkisiz/yan-etkili gruplara ayırır, her grubu (paralel veya sıralı) `WorkflowRunner` üzerinden çalıştırır ve sonuçları tek bir yanıtta birleştirir.

> **Güvenlik notu:** Her alt görev ayrı ve hedef specialist'e kısıtlı bir workflow koşusudur. Yalnızca bütün tool'ları salt-okunur ajanlar paralel çalışır; `OrderAgent` gibi karma ajanlar her zaman sıralıdır.

## Hangi amaçla kullanılır?

`CustomerSupportTeam.RunAsync`/`RunStreamingAsync`, `SubTaskOrchestrator.IsCompoundQuery(reasoning)` `true` döndürdüğünde (ör. reasoning aşaması sorguyu 2+ alt göreve ayırdıysa) bu sınıfa yönlenir; aksi halde doğrudan `WorkflowRunner` kullanılır.

## Sorumlulukları

- Alt görevleri `SubTaskOrchestrator.Partition(reasoning.SubTasks, parallelOptions)` ile paralel/sıralı gruplara bölmek.
- Paralel gruplar için `SemaphoreSlim` ile `ParallelExecutionOptions.MaxDegreeOfParallelism` sınırını uygulayıp `Task.WhenAll` ile eş zamanlı `WorkflowRunner.RunAsync`/`RunStreamingAsync` çağırmak.
- Planı `MaxSubTasks`, agent, pozitif/benzersiz sıra, geriye dönük dependency ve entity-source kurallarıyla yürütmeden önce doğrulamak.
- Her alt görev history'sini normal konuşma geçmişi ve yalnızca açıkça belirtilmiş `Dependencies` sonuçlarıyla kurmak; compound ana sorguyu ve ilgisiz kardeşleri taşımamak.
- Her alt görevin sonucunu `SubTaskOrchestrator.FormatSubTaskResult` ile etiketleyip `SortedDictionary<int, string>` (sıra numarasına göre) içinde toplamak, sonunda `SubTaskOrchestrator.AggregateSubTaskResults` ile birleştirmek.
- Tüm compound koşusuna ortak `ParallelExecutionOptions.TimeoutSeconds` bütçesi uygulamak; hata alan alt görevi `failed` işaretleyip sahte `ResponseComplete` üretmeden durmak.
- Streaming yolda `Orchestrator`/`SubTask#N` durumlarını ve deterministik birleşik metni yayınlamak.

**Üstlenmediği işler:** Tek bir alt görevin gerçek workflow koşusu (`WorkflowRunner`'a devredilir), reasoning'in sorguyu alt görevlere bölme kararı (`ReasoningService`/`SubTaskOrchestrator.IsCompoundQuery`).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `WorkflowRunner` (her alt görev bunun üzerinden çalışır), `ParallelExecutionOptions`, `IUiHintEmitter` (paralel dalların topladığı UI ipuçlarını `DrainPending` ile çekmek için — RunAsync streaming değildir, hint'ler kendiliğinden akmaz), `IApprovalContextAccessor` (paralel/sıralı her alt görev başlarken ambient ajan adını `sub.TargetAgent`'a sabitlemek için — tool çağrılarının/UI ipuçlarının doğru ajana etiketlenmesini sağlar).

**Kimler çağırır:** `CustomerSupportTeam.RunAsync`/`RunStreamingAsync` (compound query dalı).

**Ne kullanır:** `CustomerSupportBot.Application.Services.Reasoning.SubTaskOrchestrator` (`Partition`, `FormatSubTaskQuery`, `CreateSubTaskReasoning`, `FormatSubTaskResult`, `AggregateSubTaskResults`), `WorkflowResponseExtractor.ExtractDeltaText`/`SplitIntoDeltaChunks`.

## Kullanılma nedeni ve tasarım yaklaşımı

Compound query orkestrasyonu, tek-sorgu workflow koşusundan (`WorkflowRunner`) kavramsal olarak ayrı bir sorumluluk — "kaç alt görev var, hangileri paralel, sonuçlar nasıl birleşir" sorularını cevaplar, workflow'un kendisini nasıl çalıştıracağını bilmez (onu `WorkflowRunner`'a devreder). Bu ayrım, `WorkflowRunner`'ın tek-sorgu davranışını compound-query karmaşasından izole tutar; `DecomposedRunner` da `WorkflowRunner`'ı bir kara kutu olarak kullanır (yalnızca `RunAsync`/`RunStreamingAsync` imzalarını bilir).

Paralel dalda `_approvalContext.SetCurrentAgent(sub.TargetAgent)` her alt-görev task'ının kendi async akışında ayrı ayrı çağrılır — `IApprovalContextAccessor` ambient (AsyncLocal tabanlı) olduğu için, paralel çalışan görevlerin birbirinin ajan etiketini ezmemesi için bu gereklidir.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `RunDecomposedAsync(query, conversationHistory, session, reasoning, ct)` | Non-streaming compound query koşusu; birleşik nihai metni döner. |
| `RunDecomposedStreamingAsync(query, conversationHistory, session, reasoning, ct)` | Streaming compound query koşusu; `Orchestrator`/`SubTask#N` durum event'leri + alt görev sonuçlarının **ilerlemeli** `ResponseDelta` akışı + `ResponseStart`/`ResponseComplete`. |

## Streaming davranışı

Alt görev sonuçları artık turun sonunu beklemez. İki dal farklı davranır:

| Dal | Yayın |
|---|---|
| **Sıralı** grup | Başlık önce, ardından alt görevin **gerçek LLM token akışı canlı iletilir** |
| **Paralel** grup | Batch `Task.WhenAll` ile tamamlandıktan sonra sonuçlar `sub.Order` sırasıyla tek parça yayınlanır |

Paralel dalda canlı iletim mümkün değil: alt görevler eşzamanlı koşuyor ve `RunAsync`
(streaming olmayan) kullanılıyor; N akışı tek sıralı çıktıya araya girmeden örmek mümkün
olmadığı için sonuçlar batch tamamlandıktan sonra bütün hâlinde gönderilir. Sıralı dalda ise alt görevler
zaten birbiri ardına çalıştığından token'lar doğrudan akıtılabilir.

Sıralı dalda bir incelik var: alt görevin nihai metni `FormatSubTaskResult` içinde
`Trim()`'lenir, ham token'lar ise baştaki/sondaki boşluğu taşıyabilir. `TrimmingDeltaStreamer`
bu farkı akış sırasında kapatır — baştaki boşluğu atar, sondaki olabilecek boşluğu arkasından
içerik gelene kadar tutar. Sonuç, akan metnin `Trim()` uygulanmış hâlle birebir aynı olması.

Başlık, gövdeden **önce** yayınlanmak zorunda (gövde henüz üretilmedi), bu yüzden
`SubTaskOrchestrator.FormatSubTaskHeader` ayrı bir metot: başlık iki yerde ayrı yazılsaydı
akan metin ile nihai metin sessizce ayrışırdı.

Paralel gruplarda sonuçlar sırasız tamamlanır ama yayın her zaman `sub.Order` sırasındadır.

> ℹ️ Sırayı sağlayan asıl mekanizma `.OrderBy(t => t.sub.Order)` **değil**: `Task.WhenAll`
> sonuçları zaten görevlerin oluşturulma sırasında döndürür, görevler de `group.Items`
> sırasında oluşturulur. Mutasyon testiyle ölçüldü — `.OrderBy` kaldırıldığında hiçbir test
> kırılmıyor, yani o çağrı derinlemesine savunma. Sırayı gerçekten bozan bir değişiklik
> (`OrderByDescending`) ise dört testi birden kırıyor.

Yayınlanan parçaların birleşimi, `ResponseComplete`'in taşıdığı
`AggregateSubTaskResults` çıktısına **birebir eşittir** — ayırıcı iki yerde ayrı yazılmasın
diye tek sabitten okunur. `SubTaskOrchestratorTests.ProgressiveEmission_ConcatenatesTo_SameTextAsAggregate`
bu bağı kilitler; aksi hâlde yanıtın sonunda metin gözle görülür şekilde "sıçrardı".

### `ResponseStart` sıralaması ve `decomposed` bayrağı

`ResponseStart` **ilk delta'dan önce** gönderilir — yani "yanıt metni akmaya başlıyor" anlamı
tek-sorgu ve compound yollarında aynıdır.

Bu, çözülmesi gereken bir çakışmayla birlikte gelir: Blazor `response_start`'ta ajan çiplerini
**mühürler** (`Chat.razor` → `SealAgentChips`; mühürden sonra `UpsertAgentChip` hiçbir çip
eklemez). Mühürlemenin kendi gerekçesi var — tek-sorgu yolunda `ResponseAgent status=done`
olayı ilk delta'dan *sonra* geldiği için panel "kapan-aç-kapan" titremesi yapıyordu. Ama
compound'da alt görevler metin akarken de çalışmaya devam eder; erken mühür `SubTask#N`
ilerleme çiplerini tamamen yok ederdi.

Çözüm sıralamayı değil **mühürlemeyi** taşımak oldu: olay `decomposed = true` bayrağını
taşır, Blazor bunu görünce mühürlemeyi `response_complete`'e erteler (`SnapshotProcessedAgents`
orada alınır). Tek-sorgu yolu bit bit aynı kalır.

> 🐞 **Önceki hâl bir tuzaktı.** `ResponseStart` compound'da tüm metinden *sonra*
> gönderiliyordu. İşlevsel bir hata değildi (iki istemci de sıraya bakmıyordu), ama sıraya
> güvenen yeni bir tüketici — ör. "response_start geldi, spinner'ı gizle" — compound
> sorgularda sessizce yanlış çalışırdı. `ResponseStart_ComesBeforeAnyDelta_SoOrderingIsMeaningful`
> ve `ResponseStart_CarriesDecomposedFlag_SoClientDefersChipSealing` bu iki ucu birlikte kilitler.

> **Kaldırılan: sondaki yapay parçalama.** Önceden tüm alt görevler bitene kadar hiçbir
> metin gönderilmiyor, sonra birleşik metin kelime kelime 20 ms gecikmeyle "yazılıyormuş gibi"
> akıtılıyordu. Yani kullanıcı hem tüm alt görevleri bekliyor hem de üstüne 4–8 saniyelik
> sahte yazma süresi ödüyordu. Şimdi gerçek ilerleme gösteriliyor, sahte gecikme yok.

## Bağımlılıklar

Constructor injection: `WorkflowRunner runner`, `ParallelExecutionOptions parallelOptions`, `IUiHintEmitter uiHint`, `IApprovalContextAccessor approvalContext`.
