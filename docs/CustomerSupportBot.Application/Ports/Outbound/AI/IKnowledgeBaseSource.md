# IKnowledgeBaseSource / KnowledgeBaseFile

**Kaynak:** `Ports/Outbound/AI/IKnowledgeBaseSource.cs`
**Implementasyon:** [`FileSystemKnowledgeBaseSource`](../../../../CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemKnowledgeBaseSource.md)

## 1. Ne İşe Yarar

Knowledge base makalelerinin dosya sisteminden okunmasını soyutlar. `KnowledgeBaseFile`
(`RelativePath`, `Title`, `Content`) tek bir KB dosyasını temsil eden salt-veri kaydıdır.

## 2. Hangi Amaçla Kullanılır

`KnowledgeBaseIngestor` (Api katmanı, background worker) başlangıçta ve periyodik olarak bu
port üzerinden KB dosyalarını okuyup embedding üretip Qdrant'a yazar.

## 3. Sorumlulukları

- **Üstlendiği:** Dosya varlığını (`Exists`), dizin hash'ini (`ComputeDirectoryHash` — içerik
  değişip değişmediğini anlamak için), state hash okuma/yazma (`ReadStateHash`/`WriteStateHash`
  — son ingest edilen hash'i saklar) ve dosyaları akış olarak okumayı (`ReadFilesAsync`)
  üstlenir.
- **Üstlenmediği:** Embedding üretimi ([`IEmbeddingPort`](IEmbeddingPort.md)'un işi), Qdrant'a
  yazma ([`IVectorMemoryPort`](IVectorMemoryPort.md)'un işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/FileSystem/FileSystemKnowledgeBaseSource` implemente eder — `Directory`,
`File`, `SHA256` gibi somut dosya sistemi çağrıları yalnızca orada bulunur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Ingest mantığı dosya sisteminin nasıl çalıştığını (yol ayırıcıları, encoding, hash algoritması)
bilmek zorunda kalmasın diye ayrı bir port. `ComputeDirectoryHash`/`ReadStateHash` çifti,
KB içeriği değişmediyse pahalı bir re-ingest turunu (embedding + Qdrant yazımı) atlamayı sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool Exists { get; }` | KB dizini var mı? |
| `string ComputeDirectoryHash()` | Dizin dosya metadata'sı + embedding konfigürasyonundan SHA256 parmak izi üretir. |
| `string? ReadStateHash()` | Son ingest'te kaydedilen hash'i okur. |
| `void WriteStateHash(string hash)` | Yeni ingest hash'ini kaydeder. |
| `IAsyncEnumerable<KnowledgeBaseFile> ReadFilesAsync(CancellationToken ct)` | Tüm KB dosyalarını akış olarak okur. |

`KnowledgeBaseFile(string RelativePath, string Title, string Content)` — tek bir dosyanın
göreli yolu, başlığı ve tam metin içeriği.

## 7. Bağımlılıklar

Port arayüzü bağımlılıksızdır.
