# ISemanticMemoryIngestor

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/ISemanticMemoryIngestor.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`ISemanticMemoryIngestor`, Application/Services/Memory/ISemanticMemoryIngestor.cs Application-internal servis arayüzü — vector store'a toplu doküman yazma kapasitesini soyutlar. <summary>Tek dokümanı indeksten siler. Kayıt yoksa sessizce geçer (idempotent).</summary> <summary> Verilen etiketi taşımayan (ya da farklı değer taşıyan) tüm belgeleri siler. Yeniden indekslemeden sonra ARTIK ÜRETİLMEYEN belgeleri temizlemek için — bkz. <see cref="Ports.Outbound.AI.IVectorMemoryPort.DeleteWhereTagNotAsync"/>. </summary> <summary> Koleksiyondaki kayıt sayısı. Ingestion'ın "kaynak değişmedi" kısayolu, koleksiyonun gerçekten dolu olduğunu da doğrulamak zorundadır — koleksiyon dışarıdan yeniden oluşturulduğunda kaynak hash'i aynı kalır ve re-ingest sessizce atlanırdı. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ISemanticMemoryIngestor`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
