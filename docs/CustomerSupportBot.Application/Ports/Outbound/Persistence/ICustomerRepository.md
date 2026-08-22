# ICustomerRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`ICustomerRepository`, <summary> Verilen e-postanın <b>bu müşteriye</b> ait olup olmadığını söyler (büyük/küçük harf duyarsız). Müşteri yoksa veya kayıtlı e-postası yoksa <c>false</c>.  <para> Public kayıt akışının kimlik sahipliği kontrolüdür. Bu olmadan <see cref="Exists"/> yalnızca "böyle bir müşteri var mı" sorusunu yanıtlıyordu ve herkes başkasının müşteri numarasıyla hesap açıp o müşteri adına geçerli bir JWT alabiliyordu — yani sistemin geri kalanındaki tüm sahiplik kontrolleri (EntityVerifier, oturum sahipliği, tool sahiplik kuralları) doğru müşteri sanıp geçiriyordu. </para> </summary> <summary> Birden çok müşterinin adını tek sorguda getirir; bulunamayan kimlikler sonuçta yer almaz.  <para> Tekil <see cref="GetFullNameAsync"/> yerine bunun var olma sebebi N+1'dir: admin onay kuyruğu bir listedir ve her kart için ayrı sorgu atmak, kuyruk büyüdükçe panelin açılışını doğrusal olarak yavaşlatırdı. </para>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICustomerRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
