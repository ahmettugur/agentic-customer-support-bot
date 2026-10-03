# RealtimeTranscriptionConfig

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Realtime/RealtimeTranscriptionConfig.cs`
- **Tür:** `internal static class` (+ `internal enum TranscriptionModelFamily`)
- **Namespace:** `CustomerSupportBot.Adapters.AI.Realtime`

## 1. Ne İşe Yarar

Sesli oturumda kullanıcı konuşmasının transkripsiyon ayarını (`session.update` →
`audio.input.transcription`) seçilen modelin ailesine uygun biçimde üretir.

## 2. Hangi Amaçla Kullanılır

[`OpenAiRealtimeClientAdapter`](OpenAiRealtimeClientAdapter.md) oturum kurarken çağırır. Böylece
transkripsiyon modeli yalnızca appsettings'ten (`AI:Realtime:TranscriptionModel`) değiştirilerek
`gpt-4o-transcribe`, `gpt-transcribe` ve `gpt-live-transcribe` arasında geçiş yapılabilir.

## 3. Sorumlulukları

- **Üstlendiği:** model adından aileyi çıkarmak (`Classify`); aileye göre alanları seçmek
  (`Build`); uygulanamayan ayarlar için uyarı metni üretmek.
- **Üstlenmediği:** loglama (uyarıları çağıran loglar) ve transkriptin işlenmesi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [`RealtimeOptions`](../Options/AiProviderOptions.md) — `TranscriptionModel`, `TranscriptionLanguage`,
  `TranscriptionPrompt`, `TranscriptionKeywords`, `TranscriptionDelay`.
- `OpenAiRealtimeClientAdapter.BuildTranscriptionConfig` — tek tüketici.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Model aileleri yalnızca gönderilen alanlarda ayrışır:

| Aile | Örnek adlar | Dil | `prompt` | `keywords` | `delay` |
|---|---|---|---|---|---|
| `Legacy` | `gpt-4o-transcribe`, `gpt-4o-mini-transcribe-2025-12-15`, `whisper-1` | `language` | ✓ | — | — |
| `Transcribe` | `gpt-transcribe`, `gpt-transcribe-2026-07-29` | `languages` | ✓ | ✓ | — |
| `LiveTranscribe` | `gpt-live-transcribe` | `languages` | ✓ | ✓ | ✓ |

- Aile adın başından, büyük/küçük harf duyarsız çıkarılır; bilinmeyen adlar `Legacy`'ye düşer
  (bugünkü varsayılanla davranış birebir aynı kalır).
- Yeni nesle `language` gönderilmez — sağlayıcı `language` ile `languages`'ın birlikte
  gönderilmesini reddeder.
- `keywords` boşluklardan arındırılır, boşlar atılır, tekrarlar (büyük/küçük harf duyarsız) teklenir.
- `delay` yalnızca `minimal`/`low`/`medium`/`high`/`xhigh` olabilir; geçersizse gönderilmez.
- Uygulanamayan her ayar bir uyarı üretir — yanlış modelle girilmiş bir ayar sessizce etkisiz kalmaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `TranscriptionModelFamily Classify(string? model)` | Model adından aile. |
| `(Dictionary<string, object?> Config, IReadOnlyList<string> Warnings) Build(RealtimeOptions options)` | Transkripsiyon nesnesi + uyarılar. |
| `DelayValues` | Geçerli `delay` değerleri. |

## 7. Bağımlılıklar

Yok (saf fonksiyonlar).

## Bağlantılar

- [OpenAiRealtimeClientAdapter](OpenAiRealtimeClientAdapter.md)
- [AiProviderOptions](../Options/AiProviderOptions.md)
- Testler: `tests/CustomerSupportBot.Adapters.AI.Tests/RealtimeTranscriptionConfigTests.cs`
