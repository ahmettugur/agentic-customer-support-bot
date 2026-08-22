# OpenAiRealtimeClientAdapter

**Kaynak:** `CustomerSupportBot.Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.cs`
**Tür:** `public sealed class : IRealtimeVoiceTransport, IAsyncDisposable`
**Namespace:** `CustomerSupportBot.Adapters.AI.Realtime`

## 1. Ne İşe Yarar

OpenAI Realtime API'ye (`wss://api.openai.com/v1/realtime`) çift yönlü sesli WebSocket
bağlantısı kuran, ses/olay protokolünü (JSON event'ler, Base64 PCM16 ses) kapsülleyen
adaptördür. Application katmanının talep ettiği `IRealtimeVoiceTransport` portunu uygular
— üst katman ne WebSocket'i ne de OpenAI'nin JSON event şemasını bilir.

Her sesli oturum (her WebSocket bağlantısı) için **Scoped** olarak yeni bir instance
kurulur (bkz. [AiAdapterServiceCollectionExtensions](../DependencyInjection/AiAdapterServiceCollectionExtensions.md)).

## 2. Hangi Amaçla Kullanılır

Sistem sesli asistanı **iki farklı modda** çalıştırabilir, ikisi de bu sınıf üzerinden:

| Mod | Kurulum metodu | Kim konuşuyor |
|---|---|---|
| **Köprü (Bridge)** | `ConfigureBridgeSessionAsync` | Model sadece TTS/ASR köprüsü — asıl akıl yürütmeyi ayrı bir metin-tabanlı ajan yapar, bu adaptöre yalnızca "şu metni sesli oku" (`SpeakTextAsync`) denir. `create_response=false` — model kendiliğinden asla cevap üretmez. |
| **Yerel (Native)** | `ConfigureNativeSessionAsync` | Model kendi başına dinler, düşünür, function-call yapar ve sesle cevap üretir (`create_response=true`, `tools` bağlanır). Sınırlı bir görev seti vardır — bkz. §5. |

## 3. Sorumlulukları

- **Üstlendiği:**
  - `ClientWebSocket` bağlantısını açmak (`TryConnectAsync`) ve düzgün kapatmak (`CloseAsync`, `DisposeAsync`).
  - `session.update` mesajlarıyla oturumu köprü ya da yerel moda yapılandırmak.
  - Kullanıcı ses baytlarını (`SendAudioChunkAsync`) ve tool sonuçlarını (`SendToolResultsAsync`) sunucuya iletmek.
  - Kullanıcı araya girdiğinde modelin sesini kesmek (`SendInterruptAsync` → `response.cancel`).
  - Sunucudan gelen ham JSON frame'lerini okuyup (`ReceiveEventsAsync`/`TryReadFrameAsync`) strongly-typed `RealtimeServerEvent`'lere çevirmek (`ParseEvent`).
- **Üstlenmediği:** Tool'ların iş mantığını çalıştırmak (bu, Application katmanındaki realtime servisinde — `RealtimeFunctionTools` yalnızca **şema** sağlar, çağırmaz), konuşma geçmişini saklamak.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRealtimeVoiceTransport`'u implemente eder (Application katmanının portu).
- [`RealtimeFunctionTools`](RealtimeFunctionTools.md) — yerel modda modele sunulacak tool
  şemalarını (`GetToolDefinitions()`) ve isimlerini (`GetToolNames()`) sağlar; Singleton'dır
  (şemalar bağlantıdan bağımsız sabittir).
- [`AiOptions.Realtime`](../Options/AiProviderOptions.md) — model, ses, VAD süresi, transkripsiyon
  ayarları buradan okunur.
- Application katmanındaki realtime orkestrasyon servisi — `ReceiveEventsAsync`'ten gelen
  event'leri tüketip WebSocket'e (tarayıcıya) ses/metin döndürür ve `ToolCallReady`
  event'inde gerçek tool'u çağırıp `SendToolResultsAsync` ile sonucu geri yollar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**İki mod neden var:** Yerel mod düşük gecikmeli ama sınırlı (model hem dinleyip hem
düşünüp hem konuşuyor, karmaşık çok-adımlı akışlar için güvenilir değil); köprü modu
gecikmesi daha yüksek ama asıl akıl yürütmeyi güçlü bir metin modeline bırakır. Yerel
modun görev seti bu yüzden bilinçli olarak **dar tutulmuştur** — `NativeSystemInstructions`
sabiti içinde yalnızca salt-okunur sorgulara (ürün/sipariş durumu, son sipariş) izin
verilir; sipariş oluşturma/iptal/iade/şikayet gibi onay gerektiren işlemler **kasıtlı
olarak yerel moda tool olarak verilmez** — kullanıcı yazılı sohbete yönlendirilir.

**`MÜŞTERİ KİMLİĞİ SORMA` kuralı:** `NativeSystemInstructions` metninde modele açıkça
"kimlik sistemden otomatik geçiyor, asla sorma, kullanıcı başka numara söylerse yok say"
talimatı verilmiştir — bu, LLM'in kullanıcı ağzından gelen serbest metinden `customerId`
çıkarmasını (ve birinin başkasının hesabı üzerinden işlem yapabilmesini) engelleyen
prompt-seviyesi bir güvenlik önlemidir; gerçek `CustomerId` zaten JWT/context üzerinden
tool'lara geçiyor, modele hiç sorulmuyor.

> 🐞 **Bilinen sınır (kasıtlı, henüz çözülmedi):** `ConfigureNativeSessionAsync`,
> `turn_detection.create_response = true` ile kurulur — yani model, girdi guard'ı
> (`_inputGuard.Inspect` benzeri bir denetim, Application katmanında) tamamlanmadan
> **kendiliğinden** yanıt/ses üretmeye başlayabilir. Doğru çözüm `create_response = false`
> yapıp guard tamamlandıktan sonra elle `response.create` göndermek olurdu, ama bu yerel
> modun gecikme karakteristiğini değiştirir; bilinçli olarak ayrı bir tasarım kararına
> bırakılmıştır, henüz uygulanmadı.

**`TryReadFrameAsync`'in ayrı metoda çıkarılmasının nedeni:** `yield return` içeren bir
iterator metodunda (`ReceiveEventsAsync`) `try/catch` kullanılamaz (C# dil kısıtı);
bu yüzden asıl WebSocket okuma/hata yönetimi ayrı, `yield` içermeyen bir `async Task`
metoduna taşınmıştır.

## 6. Metotlar / Üyeler

| Üye | İmza | Açıklama |
|---|---|---|
| `IsEnabled` | `bool` | `RealtimeOptions.Enabled` — sesli özellik açık mı. |
| `ModelName` | `string` | Kullanılan realtime model adı (ör. `gpt-realtime-2`). |
| `Voice` | `string` | Seçili konuşma sesi. |
| `NativeToolNames` | `IReadOnlyList<string>` | Yerel moda açık tool adları (`RealtimeFunctionTools.GetToolNames()`). |
| `TryConnectAsync` | `Task<bool> TryConnectAsync(CancellationToken ct)` | WebSocket bağlantısını açar, `Authorization: Bearer` header'ı ekler. Başarısızsa loglar ve `false` döner (fırlatmaz). |
| `ConfigureBridgeSessionAsync` | `Task ConfigureBridgeSessionAsync(CancellationToken ct)` | Oturumu köprü moduna alır: `create_response=false`, sabit "sadece sesli oku" talimatı, tool yok. |
| `ConfigureNativeSessionAsync` | `Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)` | Oturumu yerel moda alır: `create_response=true`, `tools` bağlanır, `sessionContext` (müşteri adı/tarih) sistem talimatının sonuna eklenir. |
| `SendAudioChunkAsync` | `Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)` | PCM16 baytlarını Base64'e çevirip `input_audio_buffer.append` gönderir. |
| `SendInterruptAsync` | `Task SendInterruptAsync(CancellationToken ct)` | `response.cancel` gönderir — kullanıcı araya girdiğinde modelin konuşmasını kesmek için. |
| `CloseAsync` | `Task CloseAsync(string reason, CancellationToken ct)` | Bağlantı açıksa 1 saniyelik zaman aşımıyla `NormalClosure` kapatması yapar (best-effort, hatayı yutar). |
| `SpeakTextAsync` | `Task SpeakTextAsync(string text, string speakInstructions, CancellationToken ct)` | Köprü modunda kullanılır: verilen metni `conversation.item.create` ile asistan mesajı olarak ekler, ardından `response.create` ile sesli okunmasını tetikler. |
| `SendToolResultsAsync` | `Task SendToolResultsAsync(IReadOnlyList<RealtimeToolResult> results, bool triggerNextResponse, CancellationToken ct)` | Her sonucu `function_call_output` olarak gönderir; `triggerNextResponse=true` ise ardından `response.create` ile modelin devam etmesini tetikler. |
| `ReceiveEventsAsync` | `IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct)` | WebSocket'ten gelen JSON frame'leri okur, `ParseEvent` ile domain event'ine çevirip `yield return` eder; bağlantı kapanınca `ConnectionClosed` verip döngüden çıkar. |
| `DisposeAsync` | `ValueTask DisposeAsync()` | Açık bağlantıyı `NormalClosure` ile kapatmaya çalışır (best-effort), `ClientWebSocket`'i dispose eder. |

### `ParseEvent` — sunucu event eşlemesi (private static)

| OpenAI event tipi | `RealtimeServerEventType` |
|---|---|
| `input_audio_buffer.speech_started` | `SpeechStarted` |
| `input_audio_buffer.speech_stopped` | `SpeechStopped` |
| `response.created` | `ResponseCreated` |
| `conversation.item.input_audio_transcription.completed` | `InputTranscriptCompleted` (+ `Transcript`) |
| `response.output_audio.delta` | `AudioDelta` (+ Base64 çözülmüş `AudioDelta` baytları) |
| `response.output_audio_transcript.delta` | `AssistantTextDelta` (+ `TextDelta`) |
| `response.output_audio_transcript.done` | `AssistantTextDone` (+ `FullText`) |
| `response.function_call_arguments.done` | `ToolCallReady` (+ `ToolCallId`, `ToolName`, `ToolArguments`) |
| `response.done` | `ResponseDone` |
| `response.cancelled` | `ResponseCancelled` |
| `error` | `Error` (+ `ErrorMessage`) |
| `session.created`, `session.updated` | *(yok sayılır — `null` döner)* |
| diğer her şey | *(yok sayılır — `null` döner)* |

## 7. Bağımlılıklar

Constructor: `IOptions<AiOptions> aiOptions`, `RealtimeFunctionTools functionTools`,
`ILogger<OpenAiRealtimeClientAdapter> logger`.

- `_options = aiOptions.Value.Realtime` — model/ses/VAD ayarları.
- `_apiKey` — önce `Realtime.ApiKey`, boşsa `OpenAI.ApiKey`'e düşer (fallback).
- `_functionTools` — yerel mod tool şemaları.

## Bağlantılar

- [../Options/AiProviderOptions.md](../Options/AiProviderOptions.md) — `RealtimeOptions`
- [RealtimeFunctionTools.md](RealtimeFunctionTools.md) — tool şema kaynağı
- [../DependencyInjection/AiAdapterServiceCollectionExtensions.md](../DependencyInjection/AiAdapterServiceCollectionExtensions.md) — DI ömür (Scoped) kaydı
