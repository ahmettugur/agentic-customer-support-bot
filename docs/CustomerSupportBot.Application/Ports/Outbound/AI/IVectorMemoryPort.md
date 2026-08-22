# IVectorMemoryPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`IVectorMemoryPort`, <summary> Semantic memory için vektör deposu secondary port'u. </summary> <summary>Koleksiyon yoksa yaratır (idempotent).</summary> <summary>Bir veya daha fazla dokümanı koleksiyona yazar (upsert).</summary> <summary>Verilen vektöre en yakın N dokümanı döner.</summary> <summary>Tek bir noktayı siler.</summary> <summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IVectorMemoryPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
