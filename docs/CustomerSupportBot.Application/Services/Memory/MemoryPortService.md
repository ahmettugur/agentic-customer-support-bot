# MemoryPortService

**Dosya:** `Services/Memory/MemoryPortService.cs`
**Port:** `IMemoryPort` (driving/inbound port) — + yardımcı `DisabledMemoryPort` (Null Object)
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

[`SemanticMemoryService`](SemanticMemoryService.md) (sayım/arama) ile
[`IKnowledgeBaseIngestor`](IKnowledgeBaseIngestor.md) (indeksleme tetikleme)'i `IMemoryPort`
sözleşmesine bağlayan ince bir orkestrasyon katmanı — admin panelinin/tanılama uçlarının bellek
durumunu görüntülemek ve manuel indeksleme tetiklemek için kullandığı kapı.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin panel/tanılama endpoint'leri: bellek yapılandırmasını görüntülemek
(`Config`), koleksiyon boyutlarını görmek (`CountAsync`), manuel arama yapmak (`SearchAsync`,
tanılama amaçlı), manuel indeksleme tetiklemek (`IngestAsync`).

## 3. Sorumlulukları

- **Üstlendiği:** `SemanticMemoryService`/`IKnowledgeBaseIngestor` çağrılarını `IMemoryPort`
  arayüzüne yönlendirmek, ayarlardan (`SemanticMemoryOptions`) bir `MemoryConfig` DTO'su üretmek.
- **Üstlenmediği:** Belleğin gerçek iş mantığı (tamamı `SemanticMemoryService`'te).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IMemoryPort` port'unu implemente eder.
- **Inject eder:** [`SemanticMemoryService`](SemanticMemoryService.md), [`IKnowledgeBaseIngestor`](IKnowledgeBaseIngestor.md).
- **Kimin tarafından çağrılır:** Api katmanındaki admin panel/tanılama endpoint'leri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**`DisabledMemoryPort` — neden aynı dosyada, `SemanticMemoryService`'inkine paralel bir Null
Object daha var:** Semantik bellek TAMAMEN devre dışı bırakıldığında (`SemanticMemoryOptions.Enabled
= false`), DI `IMemoryPort` için bu sınıfı kaydedebilir — böylece admin panelinin bellek
ekranını render eden kod, `MemoryPortService` mi yoksa "kapalı" durumu mu olduğunu bilmeden
her zaman güvenle çağrı yapabilir (boş liste, `0` sayım, no-op ingest döner). Bu, ["`DisabledSemanticMemoryIngestor`"](DisabledSemanticMemoryIngestor.md)
ile AYNI Null Object prensibinin, bir katman yukarıda (driving port seviyesinde) tekrarıdır —
iki seviyede de "kapalı" durumun ayrı bir implementasyon olarak modellenmesi, çağıran kodun
hiçbir yerde `if (bellek açık mı?)` yazmasına gerek bırakmaz.

`Config` property'si, `SemanticMemoryOptions`'ın İÇ yapısını (embedding model/boyut,
retrieval top-K/min-score) dışarıya sade bir `MemoryConfig` DTO'su olarak sunar — Api katmanı
`SemanticMemoryOptions`'ın tam şeklini bilmek zorunda kalmaz, sadece görüntülemek istediği
alanları alır.

## 6. Metotlar / Üyeler

### `MemoryPortService`

| Üye | Açıklama |
|---|---|
| `Enabled` (`bool`) | `SemanticMemoryService.Enabled`'a delege eder. |
| `Config` (`MemoryConfig`) | Embedding model/boyut, retrieval topK/minScore'dan oluşan DTO. |
| `CountAsync(MemoryKind kind, CancellationToken ct = default): Task<long>` | Koleksiyon boyutu. |
| `SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default): Task<IReadOnlyList<MemorySearchHit>>` | Tanılama amaçlı manuel arama. |
| `IngestAsync(CancellationToken ct = default): Task` | `IKnowledgeBaseIngestor.IngestAsync`'e delege eder. |

### `DisabledMemoryPort` (Null Object)

| Üye | Açıklama |
|---|---|
| `Enabled` | Her zaman `false`. |
| `Config` | Boş/sıfır değerli `MemoryConfig`. |
| `CountAsync` | Her zaman `0`. |
| `SearchAsync` | Her zaman boş liste. |
| `IngestAsync` | No-op. |

## 7. Bağımlılıklar (Constructor Injection — yalnızca `MemoryPortService`)

- `SemanticMemoryService` — sayım/arama.
- `IKnowledgeBaseIngestor` — indeksleme tetikleme.

## Bağlantılar

- [SemanticMemoryService.md](SemanticMemoryService.md) — asıl iş mantığı
- [IKnowledgeBaseIngestor.md](IKnowledgeBaseIngestor.md) — indeksleme tetikleme sözleşmesi
- [DisabledSemanticMemoryIngestor.md](DisabledSemanticMemoryIngestor.md) — bir katman aşağıdaki paralel Null Object
