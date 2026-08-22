# FileSystemPromptRepository

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemPromptRepository.cs`
- **Tür:** `public class : IPromptRepository`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.FileSystem`

## Ne işe yarar?

`FileSystemPromptRepository`, Application katmanındaki [IPromptRepository](../../CustomerSupportBot.Application/Ports/Outbound/IPromptRepository.md) portunu uygulayan; `Prompts/` klasörü altındaki tüm `.md` sistem talimatı dosyalarını uygulama açılışında belleğe (`ConcurrentDictionary<string, string>`) yükleyen, `Get` ile saf metin ve `Render` ile `{{ANAHTAR}}` yer tutucularını dinamik değerlerle doldurarak sunan prompt yöneticisidir.

## Hangi amaçla kullanılır`?

- Tüm LLM ve çoklu ajan talimatlarını kod içine gömmek yerine Markdown dosyalarında bağımsız olarak yönetmek.
- İstek anında disk I/O maliyetini sıfırlayarak tüm prompt'ları mikro-saniye seviyesinde bellekten getirmek.
- `{{CUSTOMER_NAME}}`, `{{DATETIME}}` gibi değişkenleri düzenli ifade (`Regex`) ile güvenli şekilde ikame etmek.

## Sorumlulukları

- **Üstlendiği:**
  - `LoadAll` ile açılışta `Prompts/` dizinini taramak.
  - Dosya yolunu anahtara (`agents/planning-agent`) dönüştürmek (`BuildKey`).
  - `Get` ile saf şablonu dönmek.
  - `Render` ile şablon içerisindeki `{{PLACEHOLDER}}` alanlarını değiştirmek.

## Constructor ve Başlatma Mantığı

```csharp
public FileSystemPromptRepository(
    ILogger<FileSystemPromptRepository> logger,
    IOptions<PromptOptions>? options = null)
```

### Constructor İçerisinde Yapılan İşler:
1. `_rootDirectory = ResolveRootDirectory(options?.Value)`: Prompts dizininin mutlak yolunu çözer (`AppContext.BaseDirectory` veya `PromptOptions.RootPath`).
2. `LoadAll()` çağrılarak tüm alt dizinlerdeki `.md` dosyaları okunur ve `_prompts` sözlüğüne yazılır. `README.md` ve `NOTES.md` dosyaları atlanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Get`
```csharp
public string Get(string key)
```
- **Ne işe yarar?:** Ham prompt metnini döner. Anahtar yoksa `KeyNotFoundException` fırlatır.

### 2. `Render`
```csharp
public string Render(string key, IDictionary<string, string?>? variables = null)
```
- **Ne işe yarar?:** Şablondaki `{{ANAHTAR}}` kalıplarını verilen değişkenlerle doldurur. Sağlanmayan değişkenler boş string ile temizlenir.

## Bağımlılıklar

- [IPromptRepository](../../CustomerSupportBot.Application/Ports/Outbound/IPromptRepository.md)
- `System.Text.RegularExpressions.Regex`
