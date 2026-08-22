# RealtimeModels (RealtimeToolResult, RealtimeServerEvent, RealtimeServerEventType)

**Kaynak:** `Ports/Outbound/AI/RealtimeModels.cs`

## 1. Ne İşe Yarar

[`IRealtimeVoiceTransport`](IRealtimeVoiceTransport.md) port'unun kullandığı, sağlayıcıdan
(OpenAI vb.) bağımsız veri modellerini tanımlar: `RealtimeToolResult` (tool sonucu göndermek
için), `RealtimeServerEvent` (sağlayıcıdan gelen bir event'in vendor-nötr temsili) ve
`RealtimeServerEventType` (event türleri enum'u).

## 2. Hangi Amaçla Kullanılır

`OpenAiRealtimeClientAdapter`, OpenAI'nin JSON tabanlı event'lerini (`response.audio.delta`,
`conversation.item.input_audio_transcription.completed` vb.) bu tiplere çevirip Application
katmanına o şekilde sunar.

## 3. Sorumlulukları

- **Üstlendiği:** OpenAI'ye özgü alan adlarını (JsonNode, base64 string'ler, event adı
  string'leri) taşımayan, sağlayıcıdan bağımsız bir sözleşme sunmak.
- **Üstlenmediği:** Sağlayıcı protokolünün ayrıştırılması — bu adaptörün işidir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`IRealtimeVoiceTransport.ReceiveEventsAsync` bu tipleri yield eder;
`SendToolResultsAsync(IReadOnlyList<RealtimeToolResult>, ...)` bunları parametre olarak alır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Vendor-nötr olması bilinçlidir: `OpenAiRealtimeClientAdapter`'ın dışında hiçbir yerde OpenAI'ye
özgü JSON tipleri (`JsonNode`, base64, event string'leri) görünmez — sağlayıcı değişirse
yalnızca adaptör değişir.

## 6. Metotlar / Üyeler

**`RealtimeToolResult(string CallId, string Name, string OutputJson)`** — bir tool çağrısının
sonucunu taşır (`CallId`: hangi çağrıya ait, `Name`: tool adı, `OutputJson`: sonucun JSON
serileştirmesi).

**`RealtimeServerEvent(RealtimeServerEventType EventType)`** — sağlayıcıdan gelen tek bir olay.
Ek alanlar (hepsi nullable, `EventType`'a göre hangisi dolu olduğu değişir):

| Alan | Ne zaman dolu |
|---|---|
| `Transcript` | Kullanıcı konuşmasının transkripti tamamlandığında |
| `AudioDelta` | Modelin ses çıktısı parça parça geldiğinde |
| `TextDelta` / `FullText` | Modelin metin çıktısı (delta / tam) |
| `ErrorMessage` | `Error` event'inde |
| `ToolCallId` / `ToolName` / `ToolArguments` | Model bir tool çağırmak istediğinde |

**`RealtimeServerEventType`** enum değerleri: `SpeechStarted`, `SpeechStopped`,
`InputTranscriptCompleted`, `ResponseCreated`, `AudioDelta`, `AssistantTextDelta`,
`AssistantTextDone`, `ToolCallReady`, `ResponseDone`, `ResponseCancelled`, `Error`,
`ConnectionClosed`.

## 7. Bağımlılıklar

Yok — saf veri modelleri, dış bağımlılık taşımaz.
