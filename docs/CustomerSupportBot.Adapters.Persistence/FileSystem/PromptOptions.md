# PromptOptions

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/FileSystem/PromptOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.FileSystem`
- **Ayar bölümü:** `appsettings.json` → `Prompts` (`SectionName`)

## 1. Ne İşe Yarar

`FileSystemPromptRepository`'nin prompt dosyalarını nereden ve nasıl yükleyeceğini belirleyen yapılandırma (options) sınıfıdır.

## 2. Hangi Amaçla Kullanıldığı

Standart `Prompts/` dizini dışında bir konumdan (ör. Azure Files, NFS mount, cloud-mounted volume) prompt şablonu yüklenmesi gereken deploy senaryolarında `RootPath` ile override sağlar.

## 3. Sorumlulukları

- `RootPath` — prompt dosyalarının bulunduğu dizin (göreli verilirse `AppContext.BaseDirectory`'e göre çözümlenir).
- `AllowEmpty` — `true` ise prompt dizini boş/bulunamaz olduğunda hata fırlatmak yerine boş bir sözlükle başlama izni verir (opsiyonel lazy-loading senaryoları için; varsayılan `false` — yani normalde prompt eksikliği açılışta uygulamayı durdurur).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [FileSystemPromptRepository](FileSystemPromptRepository.md) constructor'ında `IOptions<PromptOptions>` olarak enjekte edilir ve `ResolveRootDirectory` metodunda kullanılır.
- `PersistenceAdapterServiceCollectionExtensions.AddPersistenceAdapters` içinde `services.Configure<PromptOptions>(configuration.GetSection(PromptOptions.SectionName))` ile appsettings'e bağlanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Konfigüre edilebilir kök dizin, container/cloud deploy senaryolarında build çıktısının yanında olmayan bir prompt kaynağına (ör. mount edilmiş bir volume, hot-reload edilebilir bir config kaynağı) işaret edebilmeyi sağlar — kod değişikliği olmadan sadece appsettings ile.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `const string SectionName = "Prompts"` | `appsettings.json` bölüm adı. |
| `string? RootPath` | Prompt dizini yolu; boşsa varsayılan `"Prompts"` kullanılır. |
| `bool AllowEmpty` | `true` ise dizin bulunamadığında/boşsa hata fırlatmaz. |

## 7. Bağımlılıklar

- Yok — saf POCO options sınıfı.
