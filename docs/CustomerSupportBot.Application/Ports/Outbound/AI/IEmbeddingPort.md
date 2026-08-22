# IEmbeddingPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IEmbeddingPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`IEmbeddingPort`, <summary> Metin → embedding (float[]) üreten secondary port. </summary> <summary>Tek metin için embedding üretir.</summary> <summary>Toplu embedding (KB ingest gibi pahalı işlemler için).</summary> <summary>Vektör boyutu — koleksiyon yaratırken kullanılır.</summary> <summary>Geçerli bir API key/endpoint ile çalışıyor mu? (false ise memory devre dışı kabul edilmeli.)</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IEmbeddingPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
