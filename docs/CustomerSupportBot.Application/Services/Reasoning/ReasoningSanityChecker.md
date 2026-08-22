# IReasoningSanityRule

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/ReasoningSanityChecker.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`IReasoningSanityRule`, Application/Services/ReasoningSanityChecker.cs Deterministic kural tabanlı sanity checker. Reasoning LLM'in ürettiği ReasoningResult'u VerifiedEntities ile karşılaştırır ve mantık tutarsızlıklarını yakalar. Hiçbir LLM çağrısı yapmaz — saf kurallar.  Mimari: Strategy pattern — her kural ayrı bir IReasoningSanityRule. Yeni kural eklemek için sadece sınıf yaz ve _rules listesine ekle. <summary> Tek bir sanity kuralının arayüzü. Reasoning + verified entities üzerinde çalışır, bulduğu issue'ları parametre listesine ekler. </summary> <summary>Kural kodu (issue.Code ile aynı). Logging ve test için kullanılır.</summary> <summary> Kuralı uygular. Issue tespit edilirse <paramref name="issues"/> listesine eklenir. </summary> <summary> ReasoningResult üzerinde deterministic kural tabanlı tutarsızlık taraması yapar.

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IReasoningSanityRule`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `Check`
```csharp
public List<ReasoningIssue> Check(ReasoningResult result, VerifiedEntities verified)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities verified, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities verified, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Apply`
```csharp
public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
