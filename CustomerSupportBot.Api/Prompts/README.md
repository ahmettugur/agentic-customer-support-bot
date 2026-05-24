# Prompts

Sistemde kullanılan tüm LLM prompt'ları bu dizinde markdown dosyaları olarak tutulur.
Kod içindeki `PromptService` sınıfı başlangıçta tüm `.md` dosyalarını belleğe yükler
ve `Get(key)` / `Render(key, vars)` metodlarıyla erişim sağlar.

## Klasör yapısı

- `agents/` — Her bir `ChatClientAgent`'ın `instructions` metni. 6 agent:
  - `planning-agent.md` — Gelen isteği intent'e göre analiz eder, alt görevlere böler ve ilgili agent'a yönlendirir
  - `product-inquiry-agent.md` — Ürün özellikleri, stok ve fiyat sorguları
  - `order-agent.md` — Sipariş oluşturma, durum sorgulama ve iptal işlemleri
  - `complaint-agent.md` — Şikayet kaydı oluşturma ve şikayet durumu sorgulama
  - `human-handoff-agent.md` — Çözülemeyen vakalarda insan temsilciye eskalasyon
  - `response-agent.md` — Tüm agent çıktılarını birleştirerek son kullanıcı yanıtını üretir

- `services/` — Workflow dışı servislerin prompt'ları:
  - `reasoning-system.md` — ReasoningService system prompt template; intent, güven skoru, eksik entity ve plan alanlarını yapılandırılmış JSON olarak üretir
  - `reasoning-history-note.md` — Konuşma geçmişi varsa reasoning prompt'una eklenen bağlam notu
  - `reasoning-hint.md` — PlanningAgent'a enjekte edilen ön-analiz hint'i (`{{REASONING_LINES}}` placeholder)
  - `routing-rewrite-system.md` — Dahili yönlendirme mesajını kullanıcı dostu Türkçe yanıta çevirme kuralları
  - `routing-rewrite-user.md` — Rewrite isteğinin kullanıcı şablonu; `{{ORIGINAL_QUERY}}` ve `{{ROUTING_MESSAGE}}` placeholder'ları + 5 örnek senaryo

## Placeholder sentaksı

Şablonlarda `{{ANAHTAR}}` formatında değişken yer tutucuları kullanılır. Örnek:

```
Mevcut oturum bilgisi: {{STATE_INFO}}
```

`PromptService.Render("services/reasoning-system", new Dictionary<string, string?> { ["STATE_INFO"] = "..." })`
çağrısı ile değiştirilir. Bulunamayan placeholder'lar boş string olarak değiştirilir.

## Anahtar (key) adlandırması

Dosya yolu `Prompts/` sonrasındaki göreceli yol, `.md` uzantısı atılarak kullanılır:

- `Prompts/agents/planning-agent.md` → `agents/planning-agent`
- `Prompts/services/reasoning-system.md` → `services/reasoning-system`

## Build & dağıtım

`.csproj` dosyasında `<None Update="Prompts\**\*.md"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`
kuralı ile tüm `.md` dosyaları build çıktısına kopyalanır. `PromptService` çalışma
zamanında `AppContext.BaseDirectory/Prompts` altından okur.

## Yeni prompt ekleme

1. Uygun alt klasörde yeni `.md` dosyası oluştur.
2. Placeholder'ları `{{ANAHTAR}}` sentaksıyla yerleştir.
3. Kod tarafında `promptService.Get("kategori/dosya")` veya `Render(...)` ile kullan.

Yeniden build etmek yeterli; ekstra kayıt gerekmez.
