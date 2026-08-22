# A2ASubjectIdentity

- **Kaynak:** `CustomerSupportBot.Application/Services/A2A/A2ASubjectIdentity.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.A2A`

## Ne işe yarar?

`A2ASubjectIdentity`, Application/Services/A2A/A2ASubjectIdentity.cs Özne token'ının kimlik biçimi — TEK tanım yeri. <summary> Değişimle üretilen özne token'ının kimlik (<c>sub</c>) biçimini kuran ve çözen tek yer.  <para> <b>Neden tek yerde:</b> bu biçim iki yerde kullanılıyor — token üretilirken (<see cref="A2ATokenExchangeService"/>) ve rate limit bölümlemesinde partner çıkarılırken. İki yerde ayrı ayrı elle yazılsaydı, biri değiştiğinde rate limit sessizce yanlış anahtara bölümler ve <b>partner başına sınır fiilen ortadan kalkardı</b> — hata görünür bir arıza değil, sessizce kaybolan bir koruma olurdu. </para> </summary> <summary>Özne token'ının kimliği: <c>a2a:{partnerId}:{customerId}</c>.</summary> <summary> Kimlikten partner'ı çıkarır. Biçim beklenenden farklıysa <c>null</c> döner — çağıran bunu "bilinmeyen partner" olarak ele almalı, tahmin etmemelidir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`A2ASubjectIdentity`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `BuildId`
```csharp
public static string BuildId(string partnerId, string customerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `TryGetPartnerId`
```csharp
public static string? TryGetPartnerId(string? subjectId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
