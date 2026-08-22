# IOrderToolsService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IOrderToolsService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IOrderToolsService`, <summary> Sipariş yönetimi araçları için secondary port. </summary> <summary> Tek bir siparişte <b>bir veya daha fazla</b> ürün satırı oluşturur.  <para> <paramref name="lines"/> LLM'in ürettiği ham taleptir: ürün adları doğrulanmamıştır, aynı ürün birden fazla kez geçebilir, adetler geçersiz olabilir. Doğrulama, katalog çözümlemesi ve tekilleştirme bu metodun içinde yapılır. </para> </summary> <summary> Salt-okunur ön kontrol: sipariş var mı ve login'li müşteriye ait mi? Engel varsa kullanıcıya dönecek <see cref="ToolResult"/>, yoksa <c>null</c>.  <para> HITL onaylı tool'larda (iptal/iade/şikayet) gerçek iş admin kararından SONRA çalışır; sahiplik ihlali orada yakalanırsa talep önce admin kuyruğuna düşer, admin onaylar ve işlem sessizce başarısız olur. Bu metot aynı kontrolü onay kaydı OLUŞTURULMADAN önce yapıp kullanıcıya anında geri bildirim verir ve kuyruğu kirletmez. Yürütme anındaki kontrolün YERİNE geçmez — durum iki an arasında değişebilir, ikisi birlikte çalışır.

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IOrderToolsService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
