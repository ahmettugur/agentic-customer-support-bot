# Dosya Sistemi Adaptörleri

**Dosyalar:**  
- `FileSystem/FileSystemPromptRepository.cs`  
- `FileSystem/FileSystemKnowledgeBaseSource.cs`  
- `FileSystem/PromptOptions.cs`  

Her iki adapter da InMemory ve Postgres modunda kullanılır — provider seçiminden bağımsızdır.

---

## FileSystemPromptRepository

**Port:** `IPromptRepository`  
**Yaşam döngüsü:** Singleton

Uygulama başladığında `Prompts/` dizinindeki tüm `.md` dosyalarını okuyarak in-memory cache'e yükler. Runtime'da dosya sistemi okunmaz.

### Başlatma

```csharp
// Startup'ta tüm .md dosyaları yüklenir
foreach (var file in Directory.GetFiles(promptsRoot, "*.md", SearchOption.AllDirectories))
{
    var key = Path.GetRelativePath(promptsRoot, file).Replace('\\', '/');
    _cache[key] = File.ReadAllText(file);
}
```

Cache anahtarı: `"services/routing-rewrite-user.md"` gibi kök'e göre relative path.

### `GetPromptAsync`

```csharp
Task<string?> GetPromptAsync(string relativePath, CancellationToken ct = default)
```

Cache'ten döner. Dosya bulunamazsa `null`.

### `RenderAsync`

```csharp
Task<string?> RenderAsync(string relativePath, Dictionary<string, string> vars, CancellationToken ct = default)
```

`{{PLACEHOLDER}}` formatındaki değişkenleri `vars` sözlüğünden değiştirir:

```
"Merhaba {{NAME}}, sipariş {{ORDER_ID}}"
+ vars: { "NAME": "Ahmet", "ORDER_ID": "123" }
→ "Merhaba Ahmet, sipariş 123"
```

### Thread safety

`ConcurrentDictionary` kullanır — tüm okuma/yazma işlemleri thread-safe.

### PromptOptions yapılandırması

```json
{
  "Prompts": {
    "RootPath": "Prompts",
    "AllowEmpty": false
  }
}
```

| Ayar | Açıklama |
|------|---------|
| `RootPath` | Prompt dosyalarının kök dizini (relative veya absolute) |
| `AllowEmpty` | `false` ise boş prompt bulunduğunda startup uyarısı |

Cloud-mounted path (Azure Blob mount, NFS) de desteklenir — `RootPath` absolute path verilebilir.

---

## FileSystemKnowledgeBaseSource

**Port:** `IKnowledgeBaseSource`  
**Yaşam döngüsü:** Singleton

Knowledge base dosyalarını (markdown) okuyup `SemanticMemoryService`'e sağlar. **Change detection** ile yalnızca değişen dosyalar yeniden embed edilir.

### `GetFilesAsync`

```csharp
IAsyncEnumerable<KnowledgeBaseFile> GetFilesAsync(CancellationToken ct = default)
```

`KBRoot` dizinindeki `.md`, `.txt` ve `.pdf` dosyalarını tarar. Her dosya için:

1. `SHA-256(içerik)` hesapla
2. Önceki hash ile karşılaştır
3. **Değişmişse** `KnowledgeBaseFile { Id, FileName, Content, Hash }` yayınla
4. Hash'i state dosyasına kaydet

State dosyası: `KBRoot/.kb_hashes.json`

```json
{
  "products.md": "a3f1b2c4...",
  "policies.md": "d9e2f5a8..."
}
```

### `KnowledgeBaseFile` modeli

```csharp
public record KnowledgeBaseFile(
    string Id,           // Dosya yolu (relative)
    string FileName,     // Sadece dosya adı
    string Content,      // Dosya içeriği
    string Hash          // SHA-256 hex
);
```

### Değişiklik takibi

Bu mekanizma `IngestAsync` çağrısını idempotent yapar — büyük KB'ları her seferinde yeniden embed etmek gerekmez. Yalnızca gerçekten değişen veya yeni eklenen dosyalar embed edilir.

```
Dosya yok → embed et + hash kaydet
Hash değişti → embed et + hash güncelle
Hash aynı → atla
```

### Desteklenen formatlar

| Format | İşleme |
|--------|--------|
| `.md` | Ham metin okuma |
| `.txt` | Ham metin okuma |
| `.pdf` | PDF metin ekstraksiyon (basit) |

---

## Prompt dizini yapısı

```
CustomerSupportBot.Api/
└── Prompts/
    ├── agents/
    │   ├── planning-agent.md
    │   ├── order-agent.md
    │   ├── complaint-agent.md
    │   └── ...
    └── services/
        ├── routing-rewrite-user.md
        └── ...
```

`FileSystemPromptRepository` bu yapıyı relative path ile cache'ler. Agent'lar `IPromptRepository.GetPromptAsync("agents/planning-agent.md")` şeklinde erişir.
