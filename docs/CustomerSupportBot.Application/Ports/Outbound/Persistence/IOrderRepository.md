# IOrderRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IOrderRepository`, <summary> Sipariş yönetimi için secondary port. </summary> <summary>Yeni sipariş oluşturur ve oluşturulan sipariş ID'sini döner.</summary> <summary> Stoğu düşer ve siparişi yazar — <b>tek transaction</b> içinde.  <para> Ayrı ayrı yapıldığında (önce düş, sonra yaz) ikisinin arasında oluşan herhangi bir hata stoğu düşülmüş ama karşılığında hiçbir sipariş oluşmamış hâlde bırakır. Hiçbir yerde hata görünmez; ürün stoğu sessizce ve kalıcı olarak azalır. Bu yüzden sipariş verme bölünemez bir işlemdir ve bu metot onu öyle temsil eder. </para> </summary> <summary>Sipariş ID ile sorgular. Bulunamazsa null döner.</summary> <summary>Müşterinin tüm siparişleri.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IOrderRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
