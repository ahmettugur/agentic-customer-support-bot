# Prompts

Sistemde kullanılan tüm LLM prompt'ları bu dizinde markdown dosyaları olarak tutulur.
Kod içindeki `PromptService` sınıfı başlangıçta tüm `.md` dosyalarını belleğe yükler
ve `Get(key)` / `Render(key, vars)` metodlarıyla erişim sağlar.

## Klasör yapısı

- `agents/` — Her bir `ChatClientAgent`'ın `instructions` metni. 6 agent:
  - `planning-agent.md`
  - `product-inquiry-agent.md`
  - `order-placement-agent.md`
  - `order-inquiry-agent.md`
  - `complaint-agent.md`
  - `response-agent.md`
- `services/` — Workflow dışı servislerin prompt'ları:
  - `reasoning-system.md` — ReasoningService system prompt template
  - `reasoning-history-note.md` — Geçmiş varsa eklenen bağlam notu
  - `reasoning-hint.md` — PlanningAgent'a enjekte edilen ön-analiz hint'i
  - `revision-system.md` / `revision-user.md` — RevisionService ikili prompt
  - `routing-rewrite-system.md` / `routing-rewrite-user.md` — PlanningAgent routing mesajını kullanıcıya çevirme
  - `chat-manager-selection.md` — GroupChat LLM-based next-speaker seçim prompt'u

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
