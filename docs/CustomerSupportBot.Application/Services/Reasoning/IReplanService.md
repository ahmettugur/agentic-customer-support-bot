# IReplanService

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/IReplanService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`IReplanService`, Application/Services/IReplanService.cs Application-internal strateji arayüzü — bir session için bot'un yeniden planlama yapması ve müşteriye yanıt yayınlaması use case'ini tanımlar. <summary> Replan use case: session'daki son kullanıcı mesajını yeniden değerlendirir, bot yanıtı üretir ve bridge aracılığıyla müşteriye iletir. </summary> <summary> Arka planda son müşteri mesajı için reasoning + workflow koşturur. Bot yanıtını ChatBridge üzerinden bot mesajı olarak yayınlar. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IReplanService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
