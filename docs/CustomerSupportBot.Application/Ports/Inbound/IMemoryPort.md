# IMemoryPort ve MemoryConfig

**Dosya:** `Ports/Inbound/IMemoryPort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Semantic memory (vektör bellek) dashboard ve yönetim işlemleri için primary port — kaç kayıt olduğunu saymak, arama yapmak, yeniden indekslemek.

## 2. Hangi amaçla kullanılır?

Admin panelindeki "bellek" dashboard'u, hangi tür (`MemoryKind`) kaç kayıt içerdiğini göstermek ve admin'in manuel arama/test yapabilmesi için bu portu kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Bellek durumunu (etkin mi, kaç kayıt, arama) admin'e sunmak.
- **Üstlenmediği:** Belleğin runtime'da (chat turlarında) nasıl kullanıldığı — bu iş `SemanticMemoryContextProvider`'dadır; bu port sadece yönetim/gözlem amaçlıdır.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu vektör store adaptörünü (`QdrantVectorMemoryAdapter`) kullanır.
- Admin panelindeki bellek sayfası tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`Enabled` property'si, bellek özelliği config'te kapalıyken (`Memory:Enabled=false`) diğer metotların no-op sonuç dönmesini (hata fırlatmak yerine) sağlar — admin panelinin özelliği "kapalı" olarak gösterip çökmemesini garanti eder.

## 6. Tipler ve Üyeler

### `MemoryConfig(string EmbeddingModel, int Dimension, int TopK, double MinScore)`
O an aktif embedding modelini ve arama parametrelerini (boyut, kaç sonuç, minimum benzerlik skoru) taşır — admin panelinde konfigürasyon özeti olarak gösterilir.

### `IMemoryPort`

| Üye | Açıklama |
|---|---|
| `bool Enabled { get; }` | Bellek özelliği etkin mi. |
| `MemoryConfig Config { get; }` | Aktif konfigürasyon özeti. |
| `Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)` | Belirtilen türde kaç kayıt olduğunu döner. |
| `Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default)` | Manuel test/arama. |
| `Task IngestAsync(CancellationToken ct = default)` | Yeniden indeksleme sürecini tetikler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Memory` (`MemoryKind`, `MemorySearchHit`).

## Bağlantılar

- [IKnowledgeBasePort](IKnowledgeBasePort.md) — yazma tarafı.
