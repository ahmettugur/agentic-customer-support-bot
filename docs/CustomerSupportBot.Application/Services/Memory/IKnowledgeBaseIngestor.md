# IKnowledgeBaseIngestor

**Dosya:** `Services/Memory/IKnowledgeBaseIngestor.cs`
**Tür:** `public interface` (Application-internal servis arayüzü — bir hexagonal "port" değil)
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Bilgi bankası (Knowledge Base) dosyalarını okuyup vektör belleğe indeksleme ("ingest") use
case'ini soyutlayan tek metotlu bir arayüz.

## 2. Hangi Amaçla Kullanılır

`KnowledgeBaseIngestionService` bu arayüzü implemente eder; Api katmanındaki
`KnowledgeBaseIngestor` (arka plan worker, `Workers/`) uygulama başlangıcında ve periyodik
olarak `IngestAsync`'i çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca "KB'yi indeksle" eylemini soyutlamak.
- **Üstlenmediği:** İndeksleme mantığının kendisi (implementasyonda —
  [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md)).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Implementasyonu: [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md).
- **Kimin tarafından çağrılır:** Api katmanındaki arka plan worker (`Workers/KnowledgeBaseIngestor.cs`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden "Application-internal servis arayüzü", `Ports/` klasöründe değil:** Bu proje
hexagonal mimaride `Ports/Inbound`/`Ports/Outbound` klasörlerini **dış dünyaya (Api, Adapters)
açık sözleşmeler** için kullanır. `IKnowledgeBaseIngestor` ise yalnızca Application katmanı
İÇİNDE bir soyutlama sağlar — somut `KnowledgeBaseIngestionService` sınıfını Api katmanının
arka plan worker'ından ayırmak (test edilebilirlik, DI esnekliği) için var olur, ama bir
hexagonal port'un taşıdığı "bu sınırın ötesinde bir adaptör değişebilir" anlamını taşımaz.
Bu yüzden bilinçli olarak `Services/` altında, `Ports/` altında değil.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `IngestAsync(CancellationToken ct = default): Task` | KB kaynağını okuyup vektör belleğe indeksler. |

## 7. Bağımlılıklar

Yok (arayüz).

## Bağlantılar

- [KnowledgeBaseIngestionService.md](KnowledgeBaseIngestionService.md) — implementasyon
