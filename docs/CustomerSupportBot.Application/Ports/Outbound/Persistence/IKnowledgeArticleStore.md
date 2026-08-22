# IKnowledgeArticleStore

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IKnowledgeArticleStore`, <summary> Panelden yönetilen bilgi tabanı makalelerinin kalıcılığı için secondary port. Vector store türetilmiş indekstir; kayıt otoritesi burasıdır. </summary> <summary>Yalnızca yayında olanlar — ingest ve indeksleme bunu kullanır.</summary> <summary>Kayıt yoksa false döner (çağıran 404 üretebilsin diye).</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IKnowledgeArticleStore`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
