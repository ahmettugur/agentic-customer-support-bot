# ContextPart

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ContextResult.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ContextPart`, <summary>Bir provider'ın bu turdaki sonucu — ne oldu, ne kadar yer kapladı.</summary> <summary>Bağlam üretildi ve prompt'a kondu.</summary> <summary>Provider çalıştı ama söyleyecek bir şeyi yoktu (normal durum).</summary> <summary>Hata verdi.</summary> <summary>Süresi doldu.</summary> <summary>Bütçe dolduğu için dışarıda bırakıldı.</summary> <summary> <see cref="IContextPipeline.BuildContextAsync"/> sonucu.  <para> Neden düz <c>string</c> değil: çağıranın <b>hangi provider'ın katkı yaptığını</b> bilmesi gerekiyor. Somut sebep, <c>WorkflowMessageBuilder</c>'ın konuşma geçmişini kırpma kararı — özetlenen turlar yalnızca özet BU TURDA gerçekten prompt'a girdiyse atlanabilir. Karar

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ContextPart`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `ContextResult`
```csharp
public sealed record ContextResult(string Text, IReadOnlyList<ContextPart> Parts)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Included`
```csharp
public bool Included(string providerName)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
