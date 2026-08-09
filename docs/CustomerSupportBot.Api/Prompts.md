# Prompts Dizini

## Ne İşe Yarar
LLM'lere gönderilen tüm prompt template'lerini (`*.md` dosyaları) içeren dizindir. Ajan sistem prompt'ları, reasoning talimatları ve response format tanımları burada tutulur.

## Hangi Amaçla Kullanılır
`IPromptRepository` implementasyonu bu dizindeki `.md` dosyalarını okuyarak agent'lara ve use-case'lere prompt sağlar.

## Dizin Yapısı

```
Prompts/
├── agents/
│   ├── planning-agent.md        — PlanningAgent sistem prompt'u
│   ├── product-agent.md         — ProductAgent specialist prompt
│   ├── order-agent.md           — OrderAgent specialist prompt
│   ├── complaint-agent.md       — ComplaintAgent specialist prompt
│   ├── human-handoff-agent.md   — HumanHandoffAgent prompt
│   └── response-agent.md        — ResponseAgent (final cevap, TERMINATE marker, self-critique)
├── reasoning/
│   └── reasoning-prompt.md      — ReasoningAgent için analiz talimatları
└── context/
    └── system-context.md        — Genel sistem bağlamı
```

## Diğer Katman ve Bileşenlerle İlişkileri
- **Okuyan sınıf**: `IPromptRepository` implementasyonu (Adapters katmanında).
- **Kullanan sınıflar**: `WorkflowMessageBuilder`, `SupportAgentBase` türevleri, `ReasoningService`.
- **Gizli sözleşme**: Prompt dosyalarındaki ifadeler (ör. "order_id MEVCUT") ile `WorkflowMessageBuilder`'daki entity hint metinleri arasında belgesiz bir sözleşme vardır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Prompt'lar code'dan ayrılarak `.md` dosyalarında tutulur — bu, prompt mühendisliğini derleyici değişiklikleri olmadan yapılabilir kılar. Markdown formatı okunabilirliği artırır.

## Bağımlılıklar
Yok — salt metin dosyaları.
