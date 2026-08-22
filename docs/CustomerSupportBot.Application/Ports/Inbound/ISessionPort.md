# ISessionPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/ISessionPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`ISessionPort`, <summary> Oturum yaşam döngüsü ve konuşma geçmişi için primary port. </summary> <summary> Oturum listesi. <paramref name="forCustomerId"/> verilirse yalnızca o müşterinin oturumları döner; null ise hepsi (yalnızca admin/agent için uygundur). </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ISessionPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
