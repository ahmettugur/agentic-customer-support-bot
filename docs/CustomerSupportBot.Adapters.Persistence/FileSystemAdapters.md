# Dosya Sistemi Adaptörleri

**Dosyalar:**  
- `FileSystem/FileSystemPromptRepository.cs`  
- `FileSystem/FileSystemKnowledgeBaseSource.cs`  
- `FileSystem/PromptOptions.cs`  

---

## FileSystemPromptRepository

**Port:** `IPromptRepository`  
**Yaşam döngüsü:** Singleton

Uygulama başladığında (constructor'da, `LoadAll()`) `Prompts/` dizinindeki tüm `.md` dosyalarını okuyarak `ConcurrentDictionary<string,string>` cache'e yükler. Runtime'da dosya sistemi tekrar okunmaz. `README.md`/`NOTES.md` isimli dosyalar prompt olarak yüklenmez (atlanır).

Dizin bulunamazsa `DirectoryNotFoundException`, hiç `.md` dosyası bulunamazsa `InvalidOperationException` fırlatılır — **`AllowEmpty` ayarı bu davranışı etkilemez**, kodda hiçbir yerde referans verilmez (bkz. aşağıdaki "Ölü config" notu).

### API — senkron, `Async` son eki yok

```csharp
public string Get(string key)
```

Cache'ten döner. Anahtar yoksa **`null` değil**, `KeyNotFoundException` fırlatır.

```csharp
public string Render(string key, IDictionary<string, string?>? variables = null)
```

`{{PLACEHOLDER}}` formatındaki değişkenleri `variables`'tan değiştirir; `variables` verilmezse veya boşsa placeholder'lar boş string ile temizlenir:

```
"Merhaba {{NAME}}, sipariş {{ORDER_ID}}"
+ variables: { "NAME": "Ahmet", "ORDER_ID": "123" }
→ "Merhaba Ahmet, sipariş 123"
```

Cache anahtarı, dosya yolundan uzantısız ve `/` ile normalize edilerek üretilir: `Prompts/agents/planning-agent.md` → `"agents/planning-agent"`.

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
| `RootPath` | Prompt dosyalarının kök dizini (relative veya absolute; boşsa `AppContext.BaseDirectory/Prompts` veya CWD/Prompts denenir) |
| `AllowEmpty` | ⚠️ **Ölü config** — `LoadAll()` içinde hiç referans verilmez; boş dizin durumunda her koşulda `InvalidOperationException` fırlatılır |

Cloud-mounted path (Azure Blob mount, NFS) de desteklenir — `RootPath` absolute path verilebilir.

---

## FileSystemKnowledgeBaseSource

**Port:** `IKnowledgeBaseSource`  
**Yaşam döngüsü:** Singleton

Knowledge base dosyalarını (yalnızca `.md`) okuyup `KnowledgeBaseIngestionService`'e sağlar. Change detection, **tek bir dizin-seviyesi hash** ile yapılır — dosya bazlı değil.

### `ReadFilesAsync`

```csharp
IAsyncEnumerable<KnowledgeBaseFile> ReadFilesAsync(CancellationToken ct)
```

`KnowledgeBase/` dizinindeki tüm `*.md` dosyalarını (alt dizinler dahil) okuyup her biri için `KnowledgeBaseFile` yayınlar — koşulsuz, hash karşılaştırması burada **yapılmaz**.

```csharp
public sealed record KnowledgeBaseFile(
    string RelativePath,   // "products.md" gibi kök'e göre relative yol
    string Title,          // Dosya adı (uzantısız)
    string Content         // Dosya içeriği
);
```

> `Id`/`FileName`/`Hash` alanları **yoktur**.

### `ComputeDirectoryHash` — tek dizin-seviyesi hash

```csharp
string ComputeDirectoryHash()
```

Dosya **içeriğinin** hash'i alınmaz. Bunun yerine tüm `.md` dosyalarının yolu + boyutu + `LastWriteTimeUtc` bilgisi, embedding model adı ve boyutuyla birleştirilip **tek bir SHA-256 hash** üretilir:

```
embed=text-embedding-3-large:3072;
KnowledgeBase/products.md|4821|638...;
KnowledgeBase/policies.md|2103|638...;
→ SHA-256(...) tek hex string
```

`Exists`, `ComputeDirectoryHash()`, `ReadStateHash()`, `WriteStateHash(hash)` metodlarıyla ingest servisi bu hash'i `.kb-ingest-state.txt` içinde **düz metin** (JSON değil) olarak saklar ve karşılaştırır:

```
Yeni hash == kayıtlı hash → tüm ingest atlanır
Yeni hash != kayıtlı hash → dizindeki TÜM dosyalar yeniden okunur ve embed edilir
```

Bu, dosya bazlı değil **dizin bazlı** bir "hiçbir şey değişmedi mi?" kısayoludur — herhangi bir dosya değişirse/eklenirse/silinirse tüm KB yeniden embed edilir (kısmi/dosya-seviyesi güncelleme yoktur).

### Desteklenen formatlar

Yalnızca **`.md`**. `.txt`/`.pdf` desteği yoktur.

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

`FileSystemPromptRepository` bu yapıyı relative path ile cache'ler. Servisler `IPromptRepository.Get("agents/planning-agent")` şeklinde erişir (uzantısız anahtar).
