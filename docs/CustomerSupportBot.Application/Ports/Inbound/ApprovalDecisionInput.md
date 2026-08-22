# ApprovalDecisionInput

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/ApprovalDecisionInput.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`ApprovalDecisionInput`, Ports/Driving/ApprovalDecisionInput.cs IApprovalPort driving port'unun use case input DTO'su. <summary> Admin endpoint'inin request body'si — approve/reject kararını taşır. </summary> <summary>true → approved, false → rejected.</summary> <summary>Kararı veren kişi (opsiyonel, "admin" default).</summary> <summary>Red gerekçesi (reddedilirse kullanıcıya döner).</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApprovalDecisionInput`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Approved` (`bool`): İlgili veriyi temsil eden özellik.
- `DecidedBy` (`string?`): İlgili veriyi temsil eden özellik.
- `Reason` (`string?`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
