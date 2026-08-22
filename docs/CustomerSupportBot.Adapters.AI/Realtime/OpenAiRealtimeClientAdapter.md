# OpenAiRealtimeClientAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.cs`
- **Tür:** `public sealed class : IRealtimeVoiceTransport`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Realtime`

## Ne işe yarar?

`OpenAiRealtimeClientAdapter`, OpenAI Realtime API (`wss://api.openai.com/v1/realtime`) için Application katmanının talep ettiği `IRealtimeVoiceTransport` portunu uygulayan; her sesli WebSocket bağlantısı için Scoped olarak çalışan, PCM16 24kHz çift yönlü ses akışını ve ses oturumu olaylarını yöneten adaptördür.

## Hangi amaçla kullanılır`?

- Kullanıcının mikrofonundan gelen PCM16 ham ses baytlarını Base64 formatına çevirip `input_audio_buffer.append` olayı ile OpenAI Realtime sunucusuna iletmek.
- Sunucudan gelen `response.audio.delta` ses parçalarını, `response.function_call_arguments.done` araç çağrılarını ve `input_audio_buffer.speech_started` (VAD / Kullanıcı araya girme) olaylarını ayrıştırarak Application katmanına aktarmak.
- Köprü (`ConfigureBridgeSessionAsync`) ve Yerel Ajan (`ConfigureNativeSessionAsync`) modları arasında `session.update` protokolü ile oturum yapılandırmak.

## Sorumlulukları

- **Üstlendiği:**
  - `ClientWebSocket` bağlantısını açmak (`TryConnectAsync`) ve kapatmak (`CloseAsync`).
  - Gelen WebSocket JSON çerçevelerini `ReceiveEventsAsync` ile asenkron stream etmek.
  - Kullanıcı konuşmaya başladığında OpenAI modelinin ses üretimini anında kesmek (`SendInterruptAsync`).
  - Modelin çağırdığı araçların sonuçlarını `SendToolResultAsync` ile iletmek.
- **Üstlenmediği:**
  - Araçların iş mantığını yürütmek (bu Application katmanındaki `RealtimeNativeService` servisindedir).

## Constructor ve Başlatma Mantığı

```csharp
public OpenAiRealtimeClientAdapter(
    IOptions<AiOptions> aiOptions,
    RealtimeFunctionTools functionTools,
    ILogger<OpenAiRealtimeClientAdapter> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_options`: `aiOptions.Value.Realtime` yapılandırmasını saklar (Model, Voice, Enabled vb.).
- `_apiKey`: Varsa `Realtime.ApiKey`, yoksa `OpenAI.ApiKey` değerini güvenli şekilde çözer.
- `_functionTools`: Realtime araç şemalarını sağlayan bağımlılığı saklar.
- `_logger`: Loglama motorunu saklar.

## Metotlar ve İç Çalışma Mantıkları

### 1. `TryConnectAsync`
```csharp
public async Task<bool> TryConnectAsync(CancellationToken ct)
```
- **Ne işe yarar?:** OpenAI Realtime WebSocket uç noktasına (`wss://api.openai.com/v1/realtime?model=...`) bağlantı kurar.
- **İç Mantığı:** `ClientWebSocket` örneği oluşturulur, `Authorization: Bearer <ApiKey>` header'ı eklenir ve `ConnectAsync` çağrılır. Başarılı ise `true`, hata durumunda loglayıp `false` döner.

### 2. `ConfigureNativeSessionAsync`
```csharp
public async Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)
```
- **Ne işe yarar?:** Oturumu yerel sesli asistan moduna alır (Model kendi kendine konuşur ve araç çağırabilir).
- **İç Mantığı:**
  1. `sessionContext` (müşteri adı, bugünün tarihi) sistem talimatlarının sonuna eklenir.
  2. `type: "session.update"` mesajı gönderilir.
  3. `turn_detection: semantic_vad` (Orta hassasiyet, `create_response: true`) ve `tools: _functionTools.GetToolDefinitions()` bağlanır.

### 3. `SendAudioChunkAsync`
```csharp
public async Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)
```
- **Ne işe yarar?:** Kullanıcıdan gelen PCM16 ses baytlarını `input_audio_buffer.append` JSON mesajı olarak sunucuya yazar.

### 4. `SendInterruptAsync`
```csharp
public async Task SendInterruptAsync(CancellationToken ct)
```
- **Ne işe yarar?:** Kullanıcı araya girdiğinde (Barge-in / VAD) sunucuya `response.cancel` ve `input_audio_buffer.clear` göndererek modelin konuşmasını anında durdurur.

### 5. `ReceiveEventsAsync`
```csharp
public async IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(
    [EnumeratorCancellation] CancellationToken ct)
```
- **Ne işe yarar?:** WebSocket üzerinden gelen JSON mesajlarını dinler; `AudioDelta`, `SpeechStarted`, `ToolCallCompleted`, `TextDone` gibi strongly-typed Domain olaylarına dönüştürerek yayınlar.

## Özellikler (Properties)

| Özellik | Tür | Açıklama |
|---|---|---|
| `IsEnabled` | `bool` | Realtime ses özelliğinin açık olup olmadığı. |
| `ModelName` | `string` | Kullanılan realtime model adı (ör. `gpt-4o-realtime-preview`). |
| `Voice` | `string` | Seçili konuşma sesi (ör. `alloy`, `verse`, `shimmer`). |
| `NativeToolNames` | `IReadOnlyList<string>` | Sesli oturuma açık salt-okunur araç adları. |

## Bağımlılıklar

- `System.Net.WebSockets.ClientWebSocket`
- [RealtimeFunctionTools](RealtimeFunctionTools.md)
- [AiOptions](../Options/AiProviderOptions.md)
- `CustomerSupportBot.Application.Ports.Outbound.AI.IRealtimeVoiceTransport`
