# CustomerProfileService

- **Kaynak:** `CustomerSupportBot.Application/Services/Personalization/CustomerProfileService.cs`
- **Tür:** `public  class : ICustomerProfileService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## Ne işe yarar?

`CustomerProfileService`, Application/Services/Personalization/CustomerProfileService.cs Per-customer kişiselleştirme servisi.  İki güncelleme yolu var:  1) RecordInteractionAsync (her workflow turunda otomatik) — DETERMİNİSTİK. Sıfır LLM maliyeti. Niyet frekansı, ürün ilgi alanları, dil ve son rating gibi alanları kural tabanlı günceller.  2) ConsolidateAsync (admin tetikler) — LLM ÇAĞRISI. Toplanan ham veriden kısa "Summary" + "PreferredTone" üretir; sonuç profilin özet alanlarına yazılır. Düzenli iş yükü değil; admin elle veya N turda bir tetikler.  Tasarım kararı: episodik bellek yazımı zaten her turda LLM çağırmıyor; profil de aynı maliyet sınıfında kalmalı. LLM consolidate sadece talep üzerine çalışır.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`CustomerProfileService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerProfileService(ICustomerProfileStore store,
        IGeneralChatClient chatClient,
        IAppDistributedLock distributedLock,
        IProductCatalogRepository products,
        ILogger<CustomerProfileService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `RecordInteractionAsync`
```csharp
public async Task<CustomerProfile?> RecordInteractionAsync(
        string? customerId,
        string userQuery,
        string botResponse,
        string? intent,
        int? rating = null,
        bool isNewSession = false,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ConsolidateAsync`
```csharp
public async Task<CustomerProfile?> ConsolidateAsync(string customerId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ParseTraits`
```csharp
internal static List<InferredTrait> ParseTraits(JsonElement root, string customerId, int totalTurns)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ExtractJson`
```csharp
internal static string? ExtractJson(string text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ExtractProductMentions`
```csharp
internal IReadOnlyList<string> ExtractProductMentions(string text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `LooksTurkish`
```csharp
internal static bool LooksTurkish(string text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `LooksEnglish`
```csharp
internal static bool LooksEnglish(string text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ICustomerProfileService`
