# SubTaskOrchestrator

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/SubTaskOrchestrator.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`SubTaskOrchestrator`, Application/Services/SubTaskOrchestrator.cs Compound query decomposition orkestrasyonu. Reasoning modelinin 2+ farklı specialist'e yönelen alt görev üretmesi durumunda, her biri sırayla ayrı bir workflow run olarak yürütülür ve sonuçlar birleştirilir. <summary> Compound query (bileşik sorgu) ayrıştırma orkestratörü. Reasoning sonucundaki SubTasks listesine göre her alt görevi sırayla ayrı bir workflow run olarak yürütür. </summary> <summary> Reasoning result'ın compound query olduğuna karar verir. En az 2 farklı TargetAgent olan 2+ subtask varsa decompose. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SubTaskOrchestrator`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `IsCompoundQuery`
```csharp
public static bool IsCompoundQuery(ReasoningResult? r)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CreateSubTaskReasoning`
```csharp
public static ReasoningResult CreateSubTaskReasoning(ReasoningResult parent, SubTask subTask)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ValidateExecutionPlan`
```csharp
public static List<SubTask> ValidateExecutionPlan(
        ReasoningResult reasoning,
        string originalQuery,
        ParallelExecutionOptions options)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `FormatSubTaskQuery`
```csharp
public static string FormatSubTaskQuery(SubTask subTask)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `FormatSubTaskHeader`
```csharp
public static string FormatSubTaskHeader(SubTask subTask)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `FormatSubTaskResult`
```csharp
public static string FormatSubTaskResult(SubTask subTask, string subResponse)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `AggregateSubTaskResults`
```csharp
public static string AggregateSubTaskResults(IReadOnlyList<string> parts)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Partition`
```csharp
public static List<SubTaskGroup> Partition(
        IEnumerable<SubTask> subTasks,
        ParallelExecutionOptions options)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SubTaskGroup`
```csharp
public record SubTaskGroup(bool Parallel, IReadOnlyList<SubTask> Items)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
