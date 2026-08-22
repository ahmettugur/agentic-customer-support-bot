# InputGuardVerdict

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IInputGuard.cs`
- **Tür:** `public  enum`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`InputGuardVerdict`, Application katmanında ilgili iş akışını ve domain kurallarını yürüten temel bileşendir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`InputGuardVerdict`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `InputGuardResult`
```csharp
public sealed record InputGuardResult(
    InputGuardVerdict Verdict,
    string SanitizedInput,
    IReadOnlyList<string> Flags,
    string? RejectionReason)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
