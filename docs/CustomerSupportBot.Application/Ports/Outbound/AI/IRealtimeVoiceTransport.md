# IRealtimeVoiceTransport

**Kaynak:** `Ports/Outbound/AI/IRealtimeVoiceTransport.cs`
**Implementasyon:** [`OpenAiRealtimeClientAdapter`](../../../../CustomerSupportBot.Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md)

## 1. Ne İşe Yarar

Sesli (realtime) sohbet için WebSocket tabanlı bir transport soyutlamasıdır. Application
katmanı bu port'u çağırır; adaptör hangi sağlayıcının (bugün: OpenAI Realtime API) protokolünü
konuştuğunu bilir. `IAsyncDisposable`'dır — bağlantı kapatılmadan nesne serbest bırakılamaz.

## 2. Hangi Amaçla Kullanılır

Tarayıcıdan gelen sesli chat WebSocket bağlantısı (`Api/Infrastructure` içindeki realtime
endpoint) bu port üzerinden sağlayıcıya bağlanır, ses chunk'larını iletir ve sağlayıcının
event stream'ini (transkript, ses delta'ları, tool çağrıları) okur.

## 3. Sorumlulukları

- **Üstlendiği:** Bağlantı kurma/kapama, iki farklı çalışma modunu (bridge / native) konfigüre
  etme, ses gönderme, tool sonucu gönderme, sağlayıcı event'lerini vendor-nötr
  `RealtimeServerEvent`'e çevirerek yayınlama.
- **Üstlenmediği:** OpenAI'ye özgü JSON şemasının/event isimlerinin Application katmanına
  sızması — bunlar adaptörün içinde kalır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `Adapters.AI/Realtime/OpenAiRealtimeClientAdapter` implemente eder.
- Döndürdüğü tipler [`RealtimeModels.cs`](RealtimeModels.md)'de tanımlıdır
  (`RealtimeServerEvent`, `RealtimeToolResult`).
- **Girdi korumasını bekleme (finding-12, çözüldü):** `AI:Realtime:WaitForInputGuard` (varsayılan `true`) açıkken oturum `create_response=false` ile kurulur; `RealtimeNativeService` tamamlanan transkripti `IInputGuard.Inspect`'ten geçirir ve ancak geçerse `RequestResponseAsync` (`response.create`) çağırır — reddedilen girdiye model hiç ses/araç çıktısı üretmez. Önceki yanıt sürerken istek ertelenir (yanıt bitince/iptal edilince gönderilir; aksi hâlde sağlayıcı "aktif yanıt var" hatası verir). Boş transkript (gürültü) yanıtlanmaz; transkripsiyon hatasında model yine yanıtlar (fail-open). Bedeli: yanıt transkripsiyon süresi kadar geç başlar. `false` önceki davranıştır: model konuşma biter bitmez yanıtlar, red gelince yanıt kesilir.
  Port üyeleri: `WaitsForInputGuard`, `RequestResponseAsync`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Vendor-agnostik olacak şekilde tasarlanmıştır: OpenAI, Google veya başka bir sağlayıcı ile
değiştirilebilir olması hedeflenir. Model kendi cevaplar ve tool'ları kendi çağırır.

> Eskiden ayrıca bir köprü modu için `ConfigureBridgeSessionAsync` ve `SpeakTextAsync`
> (model otomatik yanıt vermiyor, metin cevabı ayrıca seslendiriliyordu) vardı; köprü modu
> kaldırılınca port'tan da çıkarıldı.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool IsEnabled { get; }` | Realtime özelliği konfigürasyonda açık mı. |
| `string ModelName { get; }` / `string Voice { get; }` | Kullanılan model ve ses profili. |
| `IReadOnlyList<string> NativeToolNames { get; }` | Native modda tanımlı tool adları (frontend'e bilgi vermek için). |
| `Task<bool> TryConnectAsync(CancellationToken ct)` | Realtime WS'e bağlanır; başarısızsa `false`. |
| `Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)` | Sesli oturumu konfigüre eder; `sessionContext` login'li müşteri adı + tarih gibi oturuma özel talimatı sabit talimatların sonuna ekler. |
| `Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)` | Tarayıcıdan gelen PCM16 ses chunk'ını base64 encode edip sağlayıcıya iletir. |
| `Task SendInterruptAsync(CancellationToken ct)` | Devam eden yanıtı iptal eder (kullanıcı sözünü kestiğinde). |
| `Task CloseAsync(string reason, CancellationToken ct)` | WS bağlantısını kapatır. |
| `Task SendToolResultsAsync(IReadOnlyList<RealtimeToolResult> results, bool triggerNextResponse, CancellationToken ct)` | Tool sonuçlarını iletir; `triggerNextResponse=true` ise ardından `response.create` gönderir. |
| `IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct)` | Sağlayıcı event stream'ini okur; bağlantı kapanana kadar yield eder. |

## 7. Bağımlılıklar

Port arayüzü yalnızca aynı klasördeki `RealtimeModels.cs` tiplerine bağımlıdır.
