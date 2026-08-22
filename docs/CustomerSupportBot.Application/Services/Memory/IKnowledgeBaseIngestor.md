# IKnowledgeBaseIngestor

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/IKnowledgeBaseIngestor.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`IKnowledgeBaseIngestor`, <summary> KnowledgeBase ingest use case'i için Application-internal servis arayüzü. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IKnowledgeBaseIngestor`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
