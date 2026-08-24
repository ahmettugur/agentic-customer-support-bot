# ISemanticMemoryWriter

**Kaynak:** `Ports/Outbound/ISemanticMemoryWriter.cs`
**Implementasyon:** [`SemanticMemoryService`](../../Services/Memory/SemanticMemoryService.md)

## 1. Ne İşe Yarar

Semantic memory yazma port'u — adapter'ların episodik bellek (bir turun özeti) yazması için.

## 2. Hangi Amaçla Kullanılır

Bir tur bittiğinde `WorkflowRunner`/`ChatPortService` bu port'u `WriteEpisodeAsync` ile
çağırarak turun bir vektör-aranabilir "anı" olarak kaydedilmesini sağlar.

## 3. Sorumlulukları

- **Üstlendiği:** Bir turu episode olarak yazma kararının orkestrasyonu (embedding üretimi +
  vektör deposuna yazma).
- **Üstlenmediği:** Vektör üretimi ([`IEmbeddingPort`](AI/IEmbeddingPort.md)'un işi) ve
  depolama ([`IVectorMemoryPort`](AI/IVectorMemoryPort.md)'un işi) — bu servis ikisini
  koordine eder.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Memory/SemanticMemoryService` implemente eder;
[`SemanticMemoryOptions`](AI/SemanticMemoryOptions.md)'tan `Enabled` bayrağını okur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`customerId` parametresinin dokümantasyonu şunu vurgular: doğrulanmış müşteri kimliği tag
olarak yazılır ve retrieval'ın **müşteri bazında** filtrelenebilmesini sağlar — sessionId'ye
göre filtrelemek yetmez, aynı müşterinin farklı oturumlardaki (dolayısıyla farklı
sessionId'lerdeki) geçmişini birbirine bağlayamaz. Anonim turlarda `null`; o episode yalnızca
sessionId ile bulunabilir kalır — bu, login olmayan kullanıcıların verisinin başka bir
müşterinin geçmişine sızmamasını garanti eder.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool Enabled { get; }` | Semantic memory açık mı (appsettings'ten). |
| `Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery, string finalResponse, string? intent, int? rating, string? customerId = null, CancellationToken ct = default)` | Bir turu episodik bellek olarak yazar. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır (yalnızca ilkel tipler).
