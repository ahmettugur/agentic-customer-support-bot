# MemoryPortService

**Dosyalar:**  
- `Services/MemoryPortService.cs` — aktif implementasyon  
- `Services/MemoryPortService.cs` → `DisabledMemoryPort` — no-op implementasyon  

**Implements:** `IMemoryPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Semantik bellek sistemini API katmanına açar. Admin panelinden koleksiyon sayımı, arama ve knowledge base ingestion işlemleri bu port üzerinden yapılır.

---

## Constructor bağımlılıkları (MemoryPortService)

| Bağımlılık | Açıklama |
|-----------|---------|
| `SemanticMemoryService` | Vektör tabanlı bellek facade'ı |
| `IKnowledgeBaseIngestor` | KB dosya yükleme servisi |

---

## Metodlar

### `Enabled`

```csharp
bool Enabled { get; }
```

SemanticMemory aktif mi? `DisabledMemoryPort`'ta her zaman `false` döner.

---

### `Config`

```csharp
MemoryConfig Config { get; }
```

Aktif embedding modeli, boyut, TopK ve MinScore değerlerini döner:

```csharp
public record MemoryConfig(
    string EmbeddingModel,
    int EmbeddingDimension,
    int TopK,
    float MinScore);
```

---

### `CountAsync`

```csharp
Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
```

Belirtilen koleksiyondaki belge sayısını döner.

**MemoryKind seçenekleri:**

| Kind | Koleksiyon | İçerik |
|------|-----------|--------|
| `Episodic` | `episodic_memory` | Geçmiş Q&A çiftleri |
| `Lesson` | `learned_lessons` | Onaylanmış lesson'lar |
| `Knowledge` | `knowledge_base` | KB dokümanları |

---

### `SearchAsync`

```csharp
Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
    MemoryKind kind,
    string query,
    int? topK = null,
    CancellationToken ct = default)
```

Verilen sorguyu embedding'e çevirip vector store'da en yakın belgeleri arar.

---

### `IngestAsync`

```csharp
Task IngestAsync(CancellationToken ct = default)
```

`IKnowledgeBaseIngestor.IngestAsync` çağırır — KB dosyalarını yeniden yükler.

---

## `DisabledMemoryPort`

SemanticMemory devre dışıyken (`SemanticMemory.Enabled = false`) DI'a bu no-op implementasyon kaydedilir:

```csharp
Enabled         → false
Config          → MemoryConfig("", 0, 0, 0.0)
CountAsync      → Task.FromResult(0L)
SearchAsync     → Task.FromResult(Array.Empty<MemorySearchHit>())
IngestAsync     → Task.CompletedTask
```

---

## API endpoint'leri

```http
GET  /memory/status           → Enabled + Config
GET  /memory/count/{kind}     → CountAsync
POST /memory/search           → SearchAsync
POST /memory/ingest           → IngestAsync
```

---

## SemanticMemoryService ile ilişki

`MemoryPortService` doğrudan `SemanticMemoryService` concrete tipini alır (interface değil) çünkü `Enabled`, `Options` gibi servis-specific alanlarına erişmesi gerekir. Detaylı bellek mimarisi için bkz. [Memory.md](Memory.md).
