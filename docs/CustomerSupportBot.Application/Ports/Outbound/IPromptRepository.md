# IPromptRepository

**Kaynak:** `Ports/Outbound/IPromptRepository.cs`
**Implementasyon:** [`FileSystemPromptRepository`](../../../CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemPromptRepository.md)

## 1. Ne İşe Yarar

Prompt şablonlarına (`src/CustomerSupportBot.Api/Prompts/**/*.md`) erişim için secondary port.

## 2. Hangi Amaçla Kullanılır

Her ajan/servis kendi sistem promptunu bu port üzerinden ister; `{{PLACEHOLDER}}` değişkenleri
(örn. bugünün tarihi, müşteri adı) `Render` ile ikame edilir.

## 3. Sorumlulukları

- **Üstlendiği:** Prompt dosyalarının okunması ve placeholder ikamesi.
- **Üstlenmediği:** Prompt İÇERİĞİNİN ne söylediği — o `.md` dosyalarının kendisi, bu port
  yalnızca bir erişim/render katmanıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/FileSystem/FileSystemPromptRepository` implemente eder — dosya sistemine
gider, uygulama başlangıcında tüm prompt dosyalarını önbelleğe alır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Prompt'ların kod içinde string literal olarak DEĞİL, ayrı `.md` dosyalarında tutulması bilinçli
bir tasarım: prompt mühendisliği (ajan davranışını ayarlama) bir kod değişikliği/deploy
gerektirmeden yapılabilir hale gelir; `.md` uzantısı prompt'ların kendi başına okunabilir/
diff'lenebilir olmasını sağlar. Bu port sayesinde Application katmanı prompt'ların dosya
sisteminde mi, veritabanında mı, uzak bir yapılandırma servisinde mi tutulduğunu bilmez.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string Get(string key)` | Verilen anahtara karşılık gelen ham prompt metnini döner. |
| `string Render(string key, IDictionary<string, string?>? variables = null)` | Prompt'u yükler ve `{{PLACEHOLDER}}` değişkenlerini ikame eder. |
| `IReadOnlyCollection<string> Keys { get; }` | Kayıtlı tüm prompt anahtarları. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
