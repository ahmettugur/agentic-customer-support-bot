# IPromptRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IPromptRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IPromptRepository`, <summary> Prompt şablonlarına erişim için secondary port. </summary> <summary>Verilen anahtara karşılık gelen prompt'u döner.</summary> <summary>Prompt'u yükler ve {{PLACEHOLDER}} değişkenlerini ikame eder.</summary> <summary>Kayıtlı tüm prompt anahtarları.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IPromptRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
