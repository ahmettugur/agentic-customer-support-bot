# IChatPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IChatPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IChatPort`, <summary> Chat kullanım senaryosu için primary (driving) port. HTTP adaptörü (Endpoints) bu arayüze bağımlıdır; Core implementasyonuna değil. </summary> <summary> Non-streaming chat: kullanıcı sorgusunu işler ve tek JSON yanıt döndürür. </summary> <summary> SSE streaming chat: reasoning → workflow → yanıt deltalarını stream'ler. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IChatPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
