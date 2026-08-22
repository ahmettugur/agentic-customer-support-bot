# IProductCatalogRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IProductCatalogRepository`, <summary> Ürün kataloğu için secondary port. </summary> <summary>Tam eşleşme veya fuzzy match ile ürün bulur. Bulunamazsa null döner.</summary> <summary> Bir siparişin <b>tüm</b> satırlarının stoğunu tek seferde düşer.  <para> Ya hep ya hiç: satırlardan biri bile yetmezse hiçbiri düşülmez ve <see cref="StockDeductionResult.Shortages"/> yetersiz kalan satırları taşır. Çağıran taraf tek tek düşüp elle telafi etmek zorunda kalmasın diye atomiklik adapter'ın sorumluluğundadır (Postgres tarafında tek transaction). </para>  <para> <paramref name="lines"/> içindeki ürün adları <b>kanonik</b> olmalıdır (<see cref="FindProduct"/> ile çözülmüş) ve aynı ürün birden fazla satırda tekrarlanmamalıdır — tekrar, aynı satırın iki kez düşülmesi demektir. </para> </summary> <summary>Tüm ürün listesi.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IProductCatalogRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
