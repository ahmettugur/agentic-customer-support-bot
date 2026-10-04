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

Sesli asistan bu sınıf üzerinden çalışır. Oturum `ConfigureNativeSessionAsync` ile kurulur:
model kendi başına dinler, düşünür, function-call yapar ve sesle cevap üretir
(`create_response=true`, `tools` bağlanır). Sınırlı bir görev seti vardır — bkz. §5.

> Eskiden ayrıca bir **köprü modu** vardı (`ConfigureBridgeSessionAsync` + `SpeakTextAsync`):
> model yalnızca STT/TTS yapıyor, yanıtı metin tabanlı ajan hattı üretiyordu. Kaldırıldı;
> tek sesli mod budur.

## 3. Sorumlulukları

- **Üstlendiği:**
  - `ClientWebSocket` bağlantısını açmak (`TryConnectAsync`) ve düzgün kapatmak (`CloseAsync`, `DisposeAsync`).
  - `session.update` mesajıyla sesli oturumu yapılandırmak.
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

**Transkripsiyon ayarı model ailesine göre üretilir:** `audio.input.transcription` nesnesi
[`RealtimeTranscriptionConfig`](RealtimeTranscriptionConfig.md) tarafından kurulur —
`gpt-4o-transcribe` (eski nesil, `language`), `gpt-transcribe` ve `gpt-live-transcribe` (yeni nesil,
`languages` + `keywords`; canlı modelde `delay`). Model appsettings'ten değiştirilir; uygulanamayan
ayarlar için uyarı, adaptör her bağlantıda yeniden oluşturulduğu hâlde süreç başına bir kez loglanır.
Anlık parça olayları (`…input_audio_transcription.delta`) `InputTranscriptDelta` olarak iletilir —
yalnızca ekrandaki canlı altyazı içindir; kalıcı kayıt "tamamlandı" olayından yapılır (parçaların
birleşimi son metne eşit olmak zorunda değil).

**Sistem talimatı bir prompt dosyasıdır:** `Prompts/services/realtime-voice.md`
(`IPromptRepository`, anahtar `services/realtime-voice`). Eskiden bu sınıfta gömülü bir sabitti
(`NativeSystemInstructions`) ve prompt klasörünün "tüm LLM prompt'ları burada" kuralının dışında
kalıyordu; yazılı ajanların kuralları güncellendiğinde sesli taraf geride kalmıştı — ör. bir
kaydın kime ait olduğunu sızdırmama kuralı (`CUSTOMER_ID_MISMATCH`) ve `pendingApproval` alanına
göre karar verme kuralı sesli talimatta hiç yoktu. Taşınırken yazılı OrderAgent / ComplaintAgent /
HumanHandoffAgent prompt'larındaki ilgili kurallar eklendi. Oturuma özel bağlam (müşteri adı,
bugünün tarihi) `BuildSessionInstructions` ile dosya metninin sonuna `OTURUM BİLGİSİ` olarak eklenir.

**Yan etkili işlemler ve talimatlar:** Model yazılı sohbetin iş tool'larının tamamını görür.
Talimat yan etkili işlemler (sipariş, iptal, iade, şikayet kaydı) için iki
kural koyar: (1) tool'u çağırmadan önce ayrıntıları (ürün, adet, sipariş numarası) müşteriye
özetleyip **açık onayını al** — ses tanıma hata yapabilir; (2) tool sonucu "onaya gönderildi"
der, işlemi **tamamlandı diye sunma**. Asıl güvence prompt değil Application katmanındaki onay
kapısıdır (`SideEffectApprovalGate`): model ne derse desin işlem admin onayı olmadan yapılmaz.
Ödeme/fatura ve hesap/kişisel bilgi değişiklikleri için tool yoktur; kullanıcı yönlendirilir.

**`MÜŞTERİ KİMLİĞİ SORMA` kuralı:** Talimat metninde modele açıkça
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
| `ConfigureNativeSessionAsync` | `Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)` | Sesli oturumu kurar: `create_response=true`, `tools` bağlanır, talimat `BuildSessionInstructions` ile üretilir. |
| `BuildSessionInstructions` | `internal string BuildSessionInstructions(string? sessionContext)` | `services/realtime-voice` prompt'u + (varsa) `OTURUM BİLGİSİ` bölümü. |
| `SendAudioChunkAsync` | `Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)` | PCM16 baytlarını Base64'e çevirip `input_audio_buffer.append` gönderir. |
| `SendInterruptAsync` | `Task SendInterruptAsync(CancellationToken ct)` | `response.cancel` gönderir — kullanıcı araya girdiğinde modelin konuşmasını kesmek için. |
| `CloseAsync` | `Task CloseAsync(string reason, CancellationToken ct)` | Bağlantı açıksa 1 saniyelik zaman aşımıyla `NormalClosure` kapatması yapar (best-effort, hatayı yutar). |
| `SendToolResultsAsync` | `Task SendToolResultsAsync(IReadOnlyList<RealtimeToolResult> results, bool triggerNextResponse, CancellationToken ct)` | Her sonucu `function_call_output` olarak gönderir; `triggerNextResponse=true` ise ardından `response.create` ile modelin devam etmesini tetikler. |
| `ReceiveEventsAsync` | `IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct)` | WebSocket'ten gelen JSON frame'leri okur, `ParseEvent` ile domain event'ine çevirip `yield return` eder; bağlantı kapanınca `ConnectionClosed` verip döngüden çıkar. |
| `DisposeAsync` | `ValueTask DisposeAsync()` | Açık bağlantıyı `NormalClosure` ile kapatmaya çalışır (best-effort), `ClientWebSocket`'i dispose eder. |

### `ParseEvent` — sunucu event eşlemesi (internal static — `OpenAiRealtimeEventParsingTests` doğrudan sınar)

| OpenAI event tipi | `RealtimeServerEventType` |
|---|---|
| `input_audio_buffer.speech_started` | `SpeechStarted` (+ `ItemId`) |
| `input_audio_buffer.speech_stopped` | `SpeechStopped` (+ `ItemId`) |
| `conversation.item.input_audio_transcription.delta` | `InputTranscriptDelta` (+ `ItemId`, `TextDelta`) — canlı altyazı |
| `input_audio_buffer.committed` | `InputAudioCommitted` (+ `ItemId`) |
| `response.created` | `ResponseCreated` |
| `conversation.item.input_audio_transcription.completed` | `InputTranscriptCompleted` (+ `Transcript`, `ItemId`) — üç transkripsiyon ailesi de aynı olayı gönderir |
| `conversation.item.input_audio_transcription.failed` | `InputTranscriptFailed` (+ `ItemId`, `ErrorMessage`) |
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
`IPromptRepository prompts`, `ILogger<OpenAiRealtimeClientAdapter> logger`.

- `_options = aiOptions.Value.Realtime` — model/ses/VAD ayarları.
- `_apiKey` — önce `Realtime.ApiKey`, boşsa `OpenAI.ApiKey`'e düşer (fallback).
- `_functionTools` — sesli tool şemaları.
- `_prompts` — sistem talimatı (`services/realtime-voice`).

## Bağlantılar

- [../Options/AiProviderOptions.md](../Options/AiProviderOptions.md) — `RealtimeOptions`
- [RealtimeFunctionTools.md](RealtimeFunctionTools.md) — tool şema kaynağı
- [../DependencyInjection/AiAdapterServiceCollectionExtensions.md](../DependencyInjection/AiAdapterServiceCollectionExtensions.md) — DI ömür (Scoped) kaydı
