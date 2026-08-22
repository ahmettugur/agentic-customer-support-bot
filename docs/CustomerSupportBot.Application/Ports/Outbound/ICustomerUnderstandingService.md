# ICustomerUnderstandingService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ICustomerUnderstandingService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ICustomerUnderstandingService`, <summary> Memory'nin üç kaynağını (yapısal olgular, profil, çıkarımlar) tek bir <see cref="CustomerUnderstanding"/> nesnesinde sentezler.  <para> Tek doğruluk kaynağı olması bilinçli: sentez mantığı (null-kontrolleri, "tur sıfırsa gösterme" kuralı, sıralama) burada bir kez yazılır; hem bugünkü tüketici (<c>CustomerProfileContextProvider</c>) hem de ileride eklenecek bir öneri motoru aynı kuralları iki kez uygulamak zorunda kalmaz. </para> </summary> <summary> Oturumun doğrulanmış müşteri kimliği yoksa, profili yoksa veya profil hiç etkileşim görmemişse (<c>TotalTurns == 0</c>) <c>null</c> döner — "gösterilecek bir şey yok" ile "bilgi var ama boş" ayrımını çağırana bırakmaz. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICustomerUnderstandingService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
