# SubTaskOrchestrator (+ SubTaskGroup)

- **Kaynak:** `Services/Reasoning/SubTaskOrchestrator.cs`
- **Tür:** `public class` (statik yardımcı metotlar) + `public record SubTaskGroup(bool Parallel, IReadOnlyList<SubTask> Items)`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne İşe Yarar

**Compound query** (bileşik/çok parçalı sorgu) ayrıştırma mantığının tamamı — reasoning
modelinin 2+ farklı specialist ajana yönelen alt görev (`SubTask`) ürettiği durumda, bu
görevlerin doğrulanması, sıralanması/gruplanması, downstream'e iletilecek mini reasoning
sonuçlarının üretilmesi ve nihai yanıtların birleştirilmesi burada toplanır. **Statik bir
yardımcı sınıftır** — state taşımaz, DI'a kayıtlı değildir.

## 2. Hangi Amaçla Kullanılır

`ReasoningService`'in ürettiği `ReasoningResult.SubTasks` listesi "compound" ise (bkz.
`IsCompoundQuery`), `DecomposedRunner` (Adapters.Agents katmanı) her alt görevi ayrı bir
workflow run olarak yürütür. Bu sınıf o sürecin **karar verme, doğrulama, gruplama ve
birleştirme** kısımlarını sağlar — asıl yürütme `DecomposedRunner`'da olur.

## 3. Sorumlulukları

**Üstlendiği:**
- Bir reasoning sonucunun compound olup olmadığına karar vermek (`IsCompoundQuery`).
- LLM'in ürettiği alt görev planını **yürütme başlamadan önce** doğrulamak — bağımlılık
  döngüleri, ileri referanslar, bilinmeyen ajanlar, doğrulanamayan entity'ler fail-closed
  reddedilir (`ValidateExecutionPlan`).
- Alt görevleri paralel/seri gruplara ayırmak (`Partition`).
- Her alt görev için downstream'e iletilecek mini bir `ReasoningResult` üretmek
  (`CreateSubTaskReasoning`).
- Sonuçların formatlanması ve birleştirilmesi (`FormatSubTaskQuery`, `FormatSubTaskHeader`,
  `FormatSubTaskResult`, `AggregateSubTaskResults`).

**Üstlenmediği:** Alt görevlerin gerçek yürütülmesi (workflow run'ları başlatmak) —
`DecomposedRunner`'ın (Adapters.Agents) işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ReasoningResult`/`SubTask`/`VerifiedEntities` (Domain modelleri) — girdi/çıktı tipleri.
- `ParallelExecutionOptions` — hangi alt görevlerin salt-okunur (paralelleştirilebilir)
  sayıldığını belirleyen ayar nesnesi.
- [`ReasoningSanityChecker.SubTasksIgnoredRule`](ReasoningSanityChecker.md#68-subtasksignoredrule--code-subtasks_ignored) —
  `IsCompoundQuery` kapı koşulunu doğrudan kullanır.
- Tüketicisi: `DecomposedRunner` (Adapters.Agents katmanı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Neden "2+ farklı hedef ajan" kapı koşulu

`IsCompoundQuery`, yalnızca `SubTasks.Count ≥ 2` değil, **en az 2 farklı `TargetAgent`**
şartını da arar. Tek bir ajana yönelen birden fazla "adım" decompose edilmeye değmez — tek bir
workflow run zaten sırayla halledebilir. Decompose'un maliyeti (birden fazla workflow run,
birleştirme mantığı) yalnızca gerçekten farklı uzmanlık gerektiren işler için haklıdır.

### `ValidateExecutionPlan` neden bu kadar sıkı (fail-closed)

LLM'in ürettiği bir plan yürütmeye başlamadan önce **tamamen** doğrulanır: sıra numaraları
pozitif ve benzersiz mi, bağımlılıklar yalnızca **önceki** görevlere mi işaret ediyor (döngü
imkansız), her alt görev bilinen bir specialist'i mi hedefliyor, her entity değeri hem
formatça geçerli hem de ya orijinal sorguda ya da parent'ın doğrulanmış entity'lerinde
**gerçekten karşılığı var mı**. Herhangi biri başarısız olursa `InvalidOperationException`
fırlatılır — kısmi/belirsiz bir plan asla yürütülmeye başlamaz.

### `ContainsNumericToken` neden basit `Contains` değil

Bir sayısal değerin metinde "kelime sınırında" geçtiğini kontrol eder (sağındaki/solundaki
karakter rakam değilse eşleşir) — aksi halde `"104"` değeri `"1042"` içinde yanlışlıkla
eşleşebilirdi.

### `BuildVerifiedEntities` neden hep `FormatOnly`

`SubTask.Entities` (düz sözlük) decompose sırasında orijinal DB-doğrulama seviyesini kaybeder.

> 🐞 **Neden gerekli:** `subTask.Entities` yapılandırılmış (`Dictionary<string,string>`) veri
> olarak zaten elde — bunu `FormatSubTaskQuery`'nin `"açıklama (order_id=1042)"` biçimindeki
> sentetik metnine çevirip sonra tekrar ayrıştırmaya çalışmak (eskiden `IdExtractor` ile regex
> üzerinden yapılıyordu — o sınıf tamamen kaldırıldı) gereksiz bir round-trip olurdu; sentetik
> metinde "sipariş"/"müşteri" gibi Türkçe bağlam kelimeleri de olmadığından böyle bir regex
> çoğu zaman hiçbir şey bulamaz veya yanlış sınıflandırırdı. Doğrudan `VerifiedEntities`'e
> çevirmek bu riski baştan ortadan kaldırır.

### `SubTasks = []` (boş liste) `CreateSubTaskReasoning`'de neden zorunlu

Alt görev için üretilen mini `ReasoningResult`'ın kendi `SubTasks` listesi **her zaman boş**
bırakılır — aksi halde bu sonuç tekrar `IsCompoundQuery` kontrolünden geçip sonsuz bir
decompose döngüsüne girebilirdi.

### `FormatSubTaskHeader` neden `FormatSubTaskResult`'tan ayrı bir metot

`DecomposedRunner`, sıralı alt görevlerde gerçek token akışını **canlı** iletirken başlığı
gövdeden ÖNCE yayınlamak zorunda — gövde henüz üretilmemişken `FormatSubTaskResult` o anda
çağrılamaz. Başlık iki yerde ayrı yazılsaydı akan (streaming) metin ile nihai metin sessizce
ayrışırdı.

### `ResultSeparator` neden bir `const`

`DecomposedRunner` alt görev sonuçlarını kanonik sırada tek tek `response_delta` olarak
yayınlar ve aralarına bu ayırıcıyı koyar; yayınlanan parçaların birleşimi
`AggregateSubTaskResults` çıktısına **birebir eşit** olmalıdır. Ayırıcı iki yerde ayrı ayrı
yazılsaydı biri değiştiğinde ekranda akan metin ile nihai metin sessizce birbirinden ayrılırdı.

### `Partition`'ın paralel/seri gruplama mantığı

`Order`'a göre sıralı iterasyon yapılır; aynı türde (paralel/seri) ardı ardına gelen
`SubTask`'lar tek bir grupta toplanır — bu, sıralamayı (örn. read → write → read) korur.
**Bağımlılık kuralı:** `SubTask.Dependencies` ile bildirilen öncüller **asla aynı paralel
batch'e alınmaz** — `DecomposedRunner` paralel bir grubun tüm elemanlarını aynı history
snapshot'ıyla eşzamanlı başlatır, yani aynı batch'teki bir kardeşin sonucu diğerine görünmez;
öncülü önceki bir gruba düşürmek bağımlılığı karşılamak için yeterlidir (gruplar birbirini
`Task.WhenAll` ile bekler).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `IsCompoundQuery(r)` *(static)* | `r.SubTasks.Count ≥ 2` VE en az 2 farklı `TargetAgent` varsa `true`. |
| `CreateSubTaskReasoning(parent, subTask)` *(static)* | Alt görev için downstream'e geçirilecek mini bir `ReasoningResult` üretir; `Confidence=High/0.85` sabit, `SubTasks=[]`, `VerifiedEntities` `BuildVerifiedEntities` ile kurulur. |
| `ValidateExecutionPlan(reasoning, originalQuery, options)` *(static)* | Yukarıda anlatılan tüm doğrulamaları sırayla uygular; başarılıysa `Order`'a göre sıralı `List<SubTask>` döner, aksi halde `InvalidOperationException`. |
| `ValidateEntities(sub, parentEntities, originalQuery)` *(private static)* | Her entity'nin izinli anahtar kümesinde (`order_id`/`customer_id`/`complaint_id`) olduğunu, sayısal formatta olduğunu, açıklamada geçtiğini ve orijinal sorgu ya da parent'ın doğrulanmış entity'lerinde karşılığı olduğunu doğrular. |
| `BuildVerifiedEntities(entities)` *(private static)* | Düz `Dictionary<string,string>`'i `VerifiedEntities`'e çevirir, tüm alanlar `EntityVerification.FormatOnly`. |
| `FormatSubTaskQuery(subTask)` *(static)* | `"{açıklama} (order_id=1042, …)"` biçiminde downstream workflow'a iletilecek kullanıcı mesajını kurar. |
| `FormatSubTaskHeader(subTask)` *(static)* | `"**{sıra}) {açıklama}**\n\n"` — streaming sırasında gövdeden önce yayınlanabilen bağımsız başlık. |
| `FormatSubTaskResult(subTask, subResponse)` *(static)* | `FormatSubTaskHeader` + kırpılmış yanıt gövdesi. |
| `AggregateSubTaskResults(parts)` *(static)* | Parçaları `ResultSeparator` (`"\n\n---\n\n"`) ile birleştirir. |
| `Partition(subTasks, options)` *(static)* | `Order`'a göre sıralı alt görevleri paralel/seri `SubTaskGroup` listesine ayırır (yukarıdaki bağımlılık kuralına uyarak). |
| `SubTaskGroup(Parallel, Items)` *(record)* | Birlikte yürütülecek subtask grubu; `Parallel=true` ise elemanlar `Task.WhenAll` ile eşzamanlı başlatılabilir. |

## 7. Bağımlılıklar

Statik bir sınıf olduğu için kendi bağımlılığı/constructor'ı yoktur.

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — `ReasoningResult.SubTasks`'ın üreticisi
- [ReasoningSanityChecker.md](ReasoningSanityChecker.md#68-subtasksignoredrule--code-subtasks_ignored) — `IsCompoundQuery` kapı koşulunun ikinci kullanıcısı
