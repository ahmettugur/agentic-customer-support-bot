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
- 🐞 **Bilinen kısıt (finding-12, bilinçli olarak ertelendi):** native modda
  `ConfigureNativeSessionAsync` sırasında `create_response=true` gönderilir; bu, modelin
  `_inputGuard.Inspect` tamamlanmadan ses/salt-okunur-olmayan tool çıktısı üretmeye
  başlayabilmesi anlamına gelir. Düzgün çözüm `create_response=false` + elle
  `response.create` göndermek olurdu ama bu, native sesli modun gecikme karakteristiğini
  değiştirir — bilinçli olarak kullanıcıya bırakılmış bir tasarım kararı.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Vendor-agnostik olacak şekilde tasarlanmıştır: OpenAI, Google veya başka bir sağlayıcı ile
değiştirilebilir olması hedeflenir. **Bridge modu** ile **native modu** arasındaki fark önemlidir:
bridge modunda model otomatik yanıt vermez (metin cevabı ayrıca `SpeakTextAsync` ile
seslendirilir), native modda model kendi cevaplar ve tool'ları kendi çağırır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool IsEnabled { get; }` | Realtime özelliği konfigürasyonda açık mı. |
| `string ModelName { get; }` / `string Voice { get; }` | Kullanılan model ve ses profili. |
| `IReadOnlyList<string> NativeToolNames { get; }` | Native modda tanımlı tool adları (frontend'e bilgi vermek için). |
| `Task<bool> TryConnectAsync(CancellationToken ct)` | Realtime WS'e bağlanır; başarısızsa `false`. |
| `Task ConfigureBridgeSessionAsync(CancellationToken ct)` | Bridge modu session'ını konfigüre eder. |
| `Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)` | Native modu konfigüre eder; `sessionContext` login'li müşteri adı + tarih gibi oturuma özel talimatı sabit talimatların sonuna ekler. |
| `Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)` | Tarayıcıdan gelen PCM16 ses chunk'ını base64 encode edip sağlayıcıya iletir. |
| `Task SendInterruptAsync(CancellationToken ct)` | Devam eden yanıtı iptal eder (kullanıcı sözünü kestiğinde). |
| `Task CloseAsync(string reason, CancellationToken ct)` | WS bağlantısını kapatır. |
| `Task SpeakTextAsync(string text, string speakInstructions, CancellationToken ct)` | Bridge modu: asistan metnini seslendirme için gönderir. |
| `Task SendToolResultsAsync(IReadOnlyList<RealtimeToolResult> results, bool triggerNextResponse, CancellationToken ct)` | Native modu: tool sonuçlarını iletir; `triggerNextResponse=true` ise ardından `response.create` gönderir. |
| `IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct)` | Sağlayıcı event stream'ini okur; bağlantı kapanana kadar yield eder. |

## 7. Bağımlılıklar

Port arayüzü yalnızca aynı klasördeki `RealtimeModels.cs` tiplerine bağımlıdır.
