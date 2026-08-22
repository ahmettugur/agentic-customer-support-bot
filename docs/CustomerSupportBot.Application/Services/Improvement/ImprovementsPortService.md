# ImprovementsPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Improvement/ImprovementsPortService.cs`
- **Tür:** `public sealed class : IImprovementsPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Improvement`

## Ne işe yarar?

`ImprovementsPortService`, Application/Services/ImprovementsPortService.cs DRIVING PORT IMPL — IImprovementsPort → LessonMiner + ILessonStore.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ImprovementsPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ImprovementsPortService(LessonMiner miner, ILessonStore lessons)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `MineAsync`
```csharp
public Task<MiningRunReport> MineAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetLessons`
```csharp
public IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetLesson`
```csharp
public Lesson? GetLesson(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ApproveAsync`
```csharp
public Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Reject`
```csharp
public bool Reject(string id, string decidedBy, string? reason)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IImprovementsPort`
