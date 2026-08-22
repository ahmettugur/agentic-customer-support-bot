# IAgentTeamPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IAgentTeamPort`, <summary> Müşteri destek ajan takımı için secondary (driven) port. </summary> <summary> Kullanıcı sorgusunu workflow'da koşturur ve nihai yanıtı döndürür (non-streaming). </summary> <summary> Workflow'u SSE stream event'leri olarak koşturur. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IAgentTeamPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
