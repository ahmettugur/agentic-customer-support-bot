# ContextSanitizer

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/ContextSanitizer.cs`
- **Tür:** `public  class : IContextSanitizer`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`ContextSanitizer`, Application/Services/Memory/ContextSanitizer.cs IContextSanitizer implementasyonu — retrieval içeriğini prompt-injection'a karşı sertleştirir. Yazma tarafında Sanitize (Episodic bellek), okuma tarafında WrapRetrieved (KB/Lesson fence) kullanılır. Saf ve stateless — singleton kaydedilebilir. Fence kapanışı içerikte geçerse tek tırnaklı guillemet'e çevrilir — fence kırılamaz.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ContextSanitizer`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `Sanitize`
```csharp
public string Sanitize(string text, int maxLength = 2000)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `WrapRetrieved`
```csharp
public string WrapRetrieved(string text, string source)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IContextSanitizer`
