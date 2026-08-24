# LessonMiner

**Dosya:** `Services/Improvement/LessonMiner.cs`
**Tür:** `public sealed class` (+ yardımcı DTO `MiningRunReport`)
**Namespace:** `CustomerSupportBot.Application.Services.Improvement`

## 1. Ne İşe Yarar

"Self-improvement" (kendi kendine iyileşme) döngüsünün kalbi: düşük puanlı/hatalı/sorunlu
konuşma trace'lerini tarar, bunları bir LLM'e analiz ettirip **somut, admin onayı bekleyen
"Ders" (`Lesson`) önerileri** üretir. Onaylanan dersler vektör belleğe yazılır ve gelecekteki
konuşmalarda bağlam olarak devreye girer.

## 2. Hangi Amaçla Kullanılır

Periyodik olarak (veya admin panelinden manuel tetiklenerek) `MineAsync` çağrılır. Üretilen
öneriler admin panelinde listelenir; admin `ApproveAsync`/`Reject` ile karar verir.
[`ImprovementsPortService`](ImprovementsPortService.md) bu sınıfı sarmalayan driving port'tur.

## 3. Sorumlulukları

- **Üstlendiği:** Aday trace seçimi (heuristic), LLM'e analiz prompt'u hazırlamak, LLM
  cevabını JSON olarak ayrıştırmak, mükerrer ders önerilerini elemek, onaylanan dersi vektör
  belleğe yazmak.
- **Üstlenmediği:** Dersin ADMIN tarafından incelenmesi/onayı (bu bir insan kararıdır — bu
  sınıf sadece ADAY üretir), onaylanan dersin konuşmalarda gerçekten nasıl kullanılacağı (bu
  `ContextPipeline`/ilgili semantic memory provider'ın işidir — `MemoryKind.Lesson` etiketiyle
  yazılan belge, normal bir bellek belgesi gibi aranır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Inject eder:** `IReasoningTraceStore`, `IRatingStore`, `ILessonStore`, `IGeneralChatClient`
  (LLM çağrısı), `IOptions<SelfImprovementOptions>`, `ILogger`, `SemanticMemoryService?`
  (opsiyonel — bellek kapalıysa onay adımı vektör yazmadan geçer).
- **Kimin tarafından çağrılır:** [`ImprovementsPortService`](ImprovementsPortService.md)
  (`MineAsync`/`ApproveAsync`/`Reject`'i doğrudan delege eder).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden idempotent DEĞİL, admin onayı zorunlu:** LLM'in ürettiği "ders" önerileri **doğrudan**
sisteme uygulanmaz — yanlış/genellenemez bir ders (ör. tek bir kötü örnekten aşırı genelleme)
gelecekteki TÜM konuşmaları olumsuz etkileyebilir. Bu yüzden `MineAsync` sadece `Proposed`
statüsünde aday üretir; vektör belleğe yazılma (ve dolayısıyla gerçek etki) ancak
`ApproveAsync` ile, bir insan kararından SONRA gerçekleşir.

**Aday seçim heuristic'i (dört bağımsız sinyal, herhangi biri yeterli):**
1. Kullanıcının düşük puan verdiği (`Stars <= MinRatingForLesson`) oturumların trace'i.
2. `TerminationReason` "error"/"timeout" olan trace'ler (sistem hatası).
3. `SanityIssues` içinde `Error` seviyesinde bir sorun olan trace'ler.
4. `ResponseAgent`'ın kendi öz-eleştirisi (`SelfCritique.IsConcerning`) sorunlu bulduğu
   trace'ler.

> 🐞 **4. sinyal neden ayrıca eklendi:** İlk üç sinyal ancak kullanıcı ŞİKAYET ettiğinde veya
> sistem AÇIKÇA hata verdiğinde devreye girer. Ama bir yanıt teknik olarak "başarılı" dönüp
> içerik olarak kötü olabilir (robotik ton, eksik cevap, halüsinasyon riski) — kullanıcı hiç
> puan vermemiş veya fark etmemiş olabilir. `SelfCritique.IsConcerning`, `ResponseAgent`'ın
> kendi çıktısını değerlendirdiği ayrı bir sinyaldir ve bu **sessizce kötü kalan** yanıtları da
> yakalamak için eklendi.

Adaylar en yeniden eskiye sıralanıp **en fazla 8 tanesi** işlenir — token bütçesi sınırı;
LLM'e tek seferde onlarca trace göndermek maliyeti ve gecikmeyi orantısız artırır.

**Prompt tasarımı — iki spesifik düzeltme:**
- `suggestedAgent` alanı için şemada TEK bir örnek yerine **geçerli değerlerin tamamı**
  listelenir (`WellKnown.AgentNames.All`). Eskiden şemada yalnızca `"ProductAgent|null"`
  yazıyordu ve model bu tek örneğe demirlenip (anchoring), alakasız dersleri de sürekli
  `ProductAgent`'a atıyordu.
- `traces` alanı GUID trace ID'leri yerine **1-tabanlı numaralar** (`[1]`, `[2]`...) kullanır —
  LLM'e uzun GUID'leri harfiyen tekrarlatmak hem israf (token) hem hataya açıktır (bir karakter
  yanlış yazılırsa eşleşme kaybolur). Sayılar, `ResolveSourceTraces` ile kod tarafında gerçek
  ID'lere çevrilir.

**Mükerrer koruması:** Aynı trace kümesi üzerinde tarama tekrar çalıştırıldığında (ör. periyodik
job) model neredeyse aynı dersleri yeniden üretebilir. `Proposed`/`Approved` statüsündeki
mevcut başlıklarla eşleşen yeni öneriler **atlanır**. `Rejected` dersler bilinçli olarak HARİÇ
tutulur — admin bir dersi reddetmişse, aynı sorun tekrar gözlemlendiğinde yeniden önerilebilmesi
gerekir (belki ilk seferki değerlendirme yanlıştı, ya da sorun kalıcı hale geldi).

> 🐞 **`ApproveAsync`'teki `isRetry` dalı — yarım kalmış vektör yazımını kurtarma.** Bir ders
> onaylandığında hem DB'de `Approved` statüsüne geçer hem vektör belleğe yazılır. Bu iki adım
> atomik DEĞİLDİR: vektör yazımı başarısız olursa (`catch` bloğu), ders DB'de "onaylı" görünür
> ama hiçbir konuşmaya bağlam olarak GİRMEZ — pratikte etkisiz bir onaydır. `isRetry` kontrolü
> (`Status == Approved && VectorMemoryId boş`) bu durumu tespit eder ve `ApproveAsync` TEKRAR
> çağrıldığında (ör. admin panelinde "tekrar dene" ile) yalnızca vektör yazımını yeniden dener —
> `DecisionReason`'ı ezmeden.

`NormalizeAgent`, LLM'in verdiği ajan adını `WellKnown.AgentNames.All`'a karşı doğrular;
tanınmayan bir ad **`null`'a çevrilir, olduğu gibi bırakılmaz** — yanlış bir ajan etiketi (admin
panelinde rozet olarak gösterilir) boş bırakmaktan daha yanıltıcıdır, çünkü "bu dersin X ajanını
ilgilendirdiği" yanlış izlenimini verir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `MineAsync(CancellationToken ct = default): Task<MiningRunReport>` | Aday trace'leri tarar, LLM'e analiz ettirir, mükerrer olmayan önerileri `ILessonStore`'a ekler. |
| `ApproveAsync(string lessonId, string decidedBy, string? reason, CancellationToken ct = default): Task<bool>` | Dersi onaylar, vektör belleğe yazar (retry-güvenli). |
| `Reject(string lessonId, string decidedBy, string? reason): bool` | Dersi reddeder (yalnızca `Proposed` durumundaysa). |
| `NormalizeAgent(string? raw): string?` *(internal static)* | LLM'in ajan adını kanonik listeye karşı doğrular. |
| `MiningRunReport` (DTO) | `Skipped`, `SkipReason`, `Candidates`, `ProposedLessons`, `LessonIds`, `Error`. |

## 7. Bağımlılıklar (Constructor Injection)

- `IReasoningTraceStore` — son trace'leri okur.
- `IRatingStore` — düşük puanlı oturumları bulur.
- `ILessonStore` — ders önerilerinin kalıcılığı.
- `IGeneralChatClient` — analiz LLM çağrısı.
- `IOptions<SelfImprovementOptions>` — `Enabled`, `RecentTracesToScan`, `MinRatingForLesson`.
- `ILogger<LessonMiner>` — tarama/parse/vektör yazım hatalarını loglar.
- `SemanticMemoryService?` *(opsiyonel)* — onaylanan dersi vektör belleğe yazar.

## Bağlantılar

- [ImprovementsPortService.md](ImprovementsPortService.md) — bu sınıfı saran driving port
- [../Memory/SemanticMemoryService.md](../Memory/SemanticMemoryService.md) — onaylanan dersin yazıldığı vektör bellek
