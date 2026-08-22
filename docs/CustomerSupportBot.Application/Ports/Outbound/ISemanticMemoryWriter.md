# ISemanticMemoryWriter

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ISemanticMemoryWriter.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ISemanticMemoryWriter`, <summary> Semantic memory yazma port'u — adapter'ların episodik bellek yazması için. </summary> <param name="customerId"> Doğrulanmış müşteri kimliği (varsa). Tag olarak yazılır ve retrieval'ın <b>müşteri bazında</b> filtrelenebilmesini sağlar — sessionId'ye göre filtrelemek yetmez, aynı müşterinin farklı oturumlardaki (dolayısıyla farklı sessionId'lerdeki) geçmişini birbirine bağlayamaz. Anonim turlarda <c>null</c>; o episode yalnızca sessionId ile bulunabilir kalır. </param>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ISemanticMemoryWriter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
