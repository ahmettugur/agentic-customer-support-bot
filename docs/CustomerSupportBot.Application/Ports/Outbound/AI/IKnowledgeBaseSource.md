# KnowledgeBaseFile

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IKnowledgeBaseSource.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`KnowledgeBaseFile`, <summary> KnowledgeBase dosya kaynağı için secondary (driven) port. Filesystem erişimi (Directory, File, SHA) adaptör tarafında gizlenir. </summary> <summary>Dizin dosya metadata'sı + embedding konfigürasyonundan SHA256 parmakizi üretir.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`KnowledgeBaseFile`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
