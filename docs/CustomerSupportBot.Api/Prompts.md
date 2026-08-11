# Prompts Dizini

## Ne İşe Yarar
LLM'lere gönderilen tüm prompt template'lerini (`*.md` dosyaları) içeren dizindir. Ajan sistem prompt'ları, reasoning talimatları ve response format tanımları burada tutulur.

## Hangi Amaçla Kullanılır
`IPromptRepository` implementasyonu bu dizindeki `.md` dosyalarını okuyarak agent'lara ve use-case'lere prompt sağlar.

## Dizin Yapısı

```
Prompts/
├── agents/
│   ├── planning-agent.md            — PlanningAgent sistem prompt'u (routing-only, intent tespiti yapmaz)
│   ├── product-agent.md             — ProductAgent specialist prompt
│   ├── order-agent.md               — OrderAgent specialist prompt
│   ├── complaint-agent.md           — ComplaintAgent specialist prompt
│   ├── human-handoff-agent.md       — HumanHandoffAgent prompt
│   └── response-agent.md            — ResponseAgent (final cevap, TERMINATE marker, self-critique)
└── services/
    ├── reasoning-system.md          — ReasoningService'in LLM'e gönderdiği sistem prompt'u (intent enum'u BURADA)
    ├── reasoning-hint.md            — WorkflowMessageBuilder.BuildReasoningSummaryHint'in render ettiği template
    ├── reasoning-history-note.md    — Geçmiş varsa reasoning prompt'una eklenen not
    ├── routing-rewrite-system.md    — Eskalasyon routing mesajını yeniden yazan LLM çağrısı (sistem)
    └── routing-rewrite-user.md      — Aynı çağrının kullanıcı mesajı
```

## Diğer Katman ve Bileşenlerle İlişkileri
- **Okuyan sınıf**: `IPromptRepository` implementasyonu (Adapters katmanında).
- **Kullanan sınıflar**: `WorkflowMessageBuilder`, `SupportAgentBase` türevleri, `ReasoningService`.
- **Gizli sözleşme**: Prompt dosyalarındaki ifadeler (ör. "order_id MEVCUT") ile `WorkflowMessageBuilder`'daki entity hint metinleri arasında belgesiz bir sözleşme vardır. Bu sözleşmelerin bir kısmı artık `CustomerSupportBot.Api.Tests/Agents/PromptContractTests.cs` ile otomatik doğrulanıyor (aşağıya bakınız).

### `reasoning-system.md` — intent enum'u = `WellKnown.Intents` (tek doğruluk kaynağı KOD tarafında)

`reasoning-system.md`'deki `"intent": "a | b | c | ..."` satırı, reasoning LLM'inin üretebileceği **tüm** intent değerlerini tanımlar. `ReasoningResultParser` bu string'i **hiçbir normalizasyon yapmadan** taşır ve `ReasoningChatClient`'ta bir JSON schema kısıtı da olmadığı için, LLM'i tek kısıtlayan şey bu prompt metnidir. Bu enum, `WellKnown.Intents.LlmProduced` kümesiyle **birebir aynı** olmak zorundadır — biri değişip diğeri değişmezse derleme hatası vermeden sessizce kırılır (`appsettings.json`'daki `IntentSkillMap` gibi tam-string eşleşme yapan tüketiciler intent'i hiç eşleştiremez hale gelir). Bu senkron `PromptContractTests.ReasoningPrompt_IntentEnum_MatchesWellKnownIntents` ile test edilir. Detay: [`WellKnown.md`](../CustomerSupportBot.Domain/WellKnown.md#intents).

### `planning-agent.md` — kaldırılan "Compound query" bölümü

`planning-agent.md`'nin *"Compound query (çoklu niyet)"* bölümü eskiden, reasoning hint'inde `COMPOUND QUERY` notu görürse PlanningAgent'ın tüm alt görevleri tek yanıtta sırayla yönlendirmesini istiyordu. Bu talimat hiçbir zaman çalışamazdı — hem üreten kod tarafı kaldırıldı hem de PlanningAgent'ın strict JSON şeması (`ChatResponseFormat.ForJsonSchema<PlanningResult>`) zaten böyle bir çoklu-routing çıktısına izin vermiyordu. Bölüm, bileşik sorguların PlanningAgent'a ulaşmadan `DecomposedRunner` tarafından tek tek çalıştırıldığını doğru tarif eden kısa bir nota indirildi. Kod tarafındaki gerekçe: [`WorkflowMessageBuilder.md`](../CustomerSupportBot.Adapters.Agents/WorkflowMessageBuilder.md#buildreasoningsummaryhint--kaldırılan-compound-query-bloğu).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Prompt'lar code'dan ayrılarak `.md` dosyalarında tutulur — bu, prompt mühendisliğini derleyici değişiklikleri olmadan yapılabilir kılar. Markdown formatı okunabilirliği artırır.

## Bağımlılıklar
Yok — salt metin dosyaları.
