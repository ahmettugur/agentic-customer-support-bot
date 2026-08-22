# KnowledgeArticleSaveResult

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IKnowledgeBasePort.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`KnowledgeArticleSaveResult`, <summary>Makale yazma sonucu — kalıcılık başarılı olsa da indeksleme ayrı başarısız olabilir.</summary> <param name="Article">Kaydedilmiş hali (chunk sayısı güncellenmiş).</param> <param name="Indexed">Vector store'a yazıldı mı.</param> <param name="Warning">Indexed=false ise nedeni; panelde gösterilir.</param> <summary> Bilgi tabanı makalelerinin panelden yönetimi için primary (driving) port. Kaydetme/silme işlemleri vector indeksini de senkron tutar — admin'in ayrıca "yeniden ingest et" demesi gerekmez. </summary> <summary>Yeni makale oluşturur ve (yayındaysa) indeksler.</summary> <summary>Mevcut makaleyi günceller ve indeksi tazeler. Kayıt yoksa null.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`KnowledgeArticleSaveResult`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
