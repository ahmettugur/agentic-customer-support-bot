# ICustomerSupportToolsService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ICustomerSupportToolsService.cs`
- **Tür:** `public  interface : IProductToolsService, IOrderToolsService, IComplaintToolsService`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ICustomerSupportToolsService`, <summary> Müşteri destek AI araçlarının tamamını kapsayan facade port. Adapter'lar (CustomerSupportTeam, ApprovalGateService) bu arayüz üzerinden tüm tool fonksiyonlarına erişir. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICustomerSupportToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IProductToolsService, IOrderToolsService, IComplaintToolsService`
