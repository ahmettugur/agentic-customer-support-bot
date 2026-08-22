# IComplaintToolsService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IComplaintToolsService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IComplaintToolsService`, <summary> Şikayet yönetimi araçları için secondary port. </summary> <summary> Şikayet durumunu şikayet numarasıyla sorgular (SALT-OKUNUR).  <para> <paramref name="customerId"/> sahiplik kontrolü içindir ve LLM'den DEĞİL, doğrulanmış kimlikten gelir. Başkasının şikayetine erişim, "bulunamadı" ile <b>aynı metni</b> döner — bkz. <c>ComplaintToolsService</c>. </para> </summary> <summary>Müşterinin tüm şikayetlerini listeler (SALT-OKUNUR).</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IComplaintToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
