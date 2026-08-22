# LessonMiner

- **Kaynak:** `CustomerSupportBot.Application/Services/Improvement/LessonMiner.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Application.Services.Improvement`

## Ne işe yarar?

`LessonMiner`, Application/Services/Improvement/LessonMiner.cs Düşük puanlı veya hatalı trace'leri toplayıp LLM'e analiz ettirir; "Lesson" önerileri üretir. İdempotent değildir — çağıran admin onayı gerek.  Heuristic seçim: - rating ≤ MinRatingForLesson olan oturumların son trace'i - termination_reason in {"error", "timeout"} olan trace'ler - sanity issue varsa critical/error severity - ResponseAgent'ın öz-eleştirisi (SelfCritique.IsConcerning) sorun işaret ediyorsa — diğer sinyaller kullanıcı şikayetine veya sistem hatasına bağlıyken bu, sessizce kötü kalan yanıtları da yakalar  Çıktı JSON şeması: { "lessons": [ { "title", "lesson", "observation", "suggestedAgent" } ] }

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`LessonMiner`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public LessonMiner(IReasoningTraceStore traceStore,
        IRatingStore ratingStore,
        ILessonStore lessonStore,
        IGeneralChatClient chatClient,
        IOptions<SelfImprovementOptions> options,
        ILogger<LessonMiner> logger,
        SemanticMemoryService? memory = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `MineAsync`
```csharp
public async Task<MiningRunReport> MineAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ApproveAsync`
```csharp
public async Task<bool> ApproveAsync(string lessonId, string decidedBy, string? reason, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Reject`
```csharp
public bool Reject(string lessonId, string decidedBy, string? reason)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `NormalizeAgent`
```csharp
internal static string? NormalizeAgent(string? raw)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Özellikler/Properties

- `Lessons` (`List<LessonRecord>?`): İlgili veriyi temsil eden özellik.
- `Title` (`string?`): İlgili veriyi temsil eden özellik.
- `Lesson` (`string?`): İlgili veriyi temsil eden özellik.
- `Observation` (`string?`): İlgili veriyi temsil eden özellik.
- `SuggestedAgent` (`string?`): İlgili veriyi temsil eden özellik.
- `Traces` (`List<int>?`): İlgili veriyi temsil eden özellik.
- `Skipped` (`bool`): İlgili veriyi temsil eden özellik.
- `SkipReason` (`string?`): İlgili veriyi temsil eden özellik.
- `Candidates` (`int`): İlgili veriyi temsil eden özellik.
- `ProposedLessons` (`int`): İlgili veriyi temsil eden özellik.
- `LessonIds` (`List<string>`): İlgili veriyi temsil eden özellik.
- `Error` (`string?`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
