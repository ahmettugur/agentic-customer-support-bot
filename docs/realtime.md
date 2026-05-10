# Sesli Konuşma Modu (Realtime)

OpenAI **Realtime API** (`gpt-realtime-2`) üzerinden kurulan full-duplex ses kanalları. Sistem **iki ayrı sesli mod** sunar:

## İki Mod Özeti

| Mod | Buton | Endpoint | gpt-realtime-2 rolü | Agent pipeline | Tool seti |
|---|---|---|---|---|---|
| **🎤 Sesli Asistan (köprü)** | Mikrofon | `/chat/realtime` | Yalnızca STT + TTS köprüsü (`create_response: false`) | **Tam akış** — reasoning + 7 ajan + HITL | Tüm tool'lar (sipariş aç, şikayet, vb.) |
| **⚡ Hızlı Sesli (native)** | Şimşek | `/chat/realtime-native` | Modelin kendisi konuşur ve tool çağırır (`create_response: true`) | **YOK** — model tek başına yanıtlar | Yalnızca okuma-only (5 tool — veda dahil) |

**Hangisini ne zaman?**

- **Sipariş oluşturma**, **şikayet kaydı**, **iade**, **karmaşık akış** → köprü modu (HITL korunur)
- **"ORD-1 nerede?"**, **"iPhone fiyatı?"**, **"son siparişim ne?"** → native mod (3-4× daha hızlı, ~70% daha ucuz)

Kullanıcı yan-etkili bir işlem isterse native modda model **tool çağırmaz**, kibarca yazılı sohbete yönlendirir.

---

## Mimari

```
┌─────────────────────────────────────────────────────────────────────┐
│ FRONTEND (wwwroot/)                                                 │
│ ┌──────────────────┐    ┌──────────────────┐   ┌─────────────────┐ │
│ │ Mikrofon         │    │ realtime-client  │   │ chat-ui         │ │
│ │ getUserMedia +   │───▶│ WebSocket client │   │ (aynı UI'yi     │ │
│ │ AudioWorklet     │    │ + PCM16 playback │   │  tekrar kullan) │ │
│ │ (24kHz capture)  │    │                  │   │                 │ │
│ └──────────────────┘    └────────┬─────────┘   └─────────────────┘ │
└─────────────────────────────┬─── │ ws://.../chat/realtime ──────────┘
                              │    │
                              ▼    ▼
┌─────────────────────────────────────────────────────────────────────┐
│ BACKEND — RealtimeBridge.cs                                         │
│                                                                     │
│  Browser WS  ◀──────────┐    ┌────────▶  OpenAI Realtime WS       │
│  (binary PCM16 +        │    │            (wss://api.openai.com/   │
│   JSON control)         │    │             v1/realtime?model=...)  │
│                         ▼    │                                      │
│   ┌──────────────────────────┐                                      │
│   │ PumpBrowserToOpenAi      │  (input audio forward)              │
│   │ PumpOpenAiToBrowser      │  (output audio + transcript)        │
│   │ HandleBrowserControl     │  (interrupt / stop)                 │
│   │ HandleUserTranscript     │──────┐                              │
│   └──────────────────────────┘      │                              │
│                                     ▼                              │
│            ┌──────────────────────────────────────┐                │
│            │ ReasoningService.ReasonStreamingAsync│                │
│            │          │                            │                │
│            │          ▼                            │                │
│            │ CustomerSupportTeam.RunStreamingAsync │  (aynı MAF     │
│            │          │                            │   pipeline'ı —  │
│            │          ▼                            │   text chat ile │
│            │ StreamEvent'ler (reasoning_*, agent, │   paylaşılır)   │
│            │  response_*, done)                    │                │
│            └──────────────────────────────────────┘                │
│                   │ JSON olarak browser'a yansıtılır (aynı tipler) │
│                   │                                                │
│                   ▼                                                │
│             Son metin → SpeakAsync() → OpenAI Realtime TTS         │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Akış — tipik bir tur

```
1. [Kullanıcı mikrofon butonuna basar]
2. Tarayıcı getUserMedia + AudioWorkletNode ile 24kHz PCM16 stream başlatır
3. Backend WebSocket kurulur → /chat/realtime
   - RealtimeBridge OpenAI Realtime'a paralel WS açar
   - session.update ile: nested audio.input/output, VAD (semantic_vad + eagerness), transcription config, create_response=false
   - { type:"connected", sessionId:"..." } → browser
4. Kullanıcı konuşur
   - Her audio chunk browser → backend → OpenAI forward edilir
   - Server-side VAD konuşma sonunu algılar
   - OpenAI: conversation.item.input_audio_transcription.completed → transcript hazır
5. RealtimeBridge.HandleUserTranscriptAsync tetiklenir:
   a. Input guard (prompt injection / sanitization)
   b. { type:"workflow_start" } browser'a
   c. ReasonStreamingAsync → reasoning_start/delta/complete event'leri forward
   d. RunStreamingAsync → agent + response_delta event'leri forward
   e. SessionManager.AddExchange (kalıcı history)
   f. { type:"workflow_done" } browser'a
6. Frontend her event'te text chat UI'sini günceller:
   - reasoning panel (intent, steps, entity'ler, sentiment)
   - agent chip ("● Planlama Ajanı çalışıyor…" → "✓ tamamlandı")
   - bot bubble'a token-by-token metin akışı
   - final markdown render
7. Backend SpeakAsync ile aynı metni OpenAI'ye gönderir:
   - conversation.item.create (role=assistant, content=final text)
   - response.create → OpenAI TTS audio delta'ları üretir
8. Backend TTS audio'yu browser'a forward eder
   - Browser PCM16 chunk'ları AudioContext üzerinden çalar
9. response.done → state='listening', yeni tur hazır
```

---

## Event kontratı — WebSocket mesajları

**Browser → Backend**:

| Mesaj | İçerik | Anlam |
|---|---|---|
| `Binary frame` | PCM16 24kHz | Mikrofon ses chunk'ı (pipe through to OpenAI) |
| `{type:"interrupt"}` | — | Kullanıcı asistanı keser → `response.cancel` |
| `{type:"stop"}` | — | Oturumu kapat |

**Backend → Browser**:

| Event | Payload | Anlam |
|---|---|---|
| `connected` | `{sessionId}` | WS kuruldu, agent session hazır |
| `Binary frame` | PCM16 24kHz | TTS output chunk'ı (AudioContext'e yolla) |
| `workflow_start` | — | Agent pipeline tetiklendi |
| `user_transcript` | `{text}` | ASR transcript'i hazır (UI'da user bubble) |
| `conversation_ended` | `{reason}` | Görüşme sonlandırıldı (tool veya idle timeout) |
| `reasoning_start` | — | Reasoning fazı başladı (💭 Düşünüyor) |
| `reasoning_delta` | `{text}` | Reasoning token chunk'ı |
| `reasoning_complete` | `ReasoningResult` | Panel final (intent, steps, entities…) |
| `agent` | `{name, status, parallel?}` | Aktif ajan rozeti güncelle |
| `response_start` | `{terminationReason}` | Yanıt akışı başlıyor |
| `response_delta` | `{text}` | Yanıt token chunk'ı (bubble'a yapış) |
| `response_complete` | `{text, terminationReason}` | Yanıt bitti (markdown render) |
| `workflow_done` | — | Tüm agent akışı tamam, TTS başlayacak |
| `response_done` | — | TTS akışı tamamlandı, yeni tura hazır |
| `error` | `{message}` veya `{data}` | Sistem hatası (message) / pipeline hatası (data) |

**Kritik**: `reasoning_*`, `agent`, `response_*`, `done` event isimleri **text chat SSE** ile birebir aynıdır → frontend aynı `ChatApp._handleStreamEvent` handler'ını yeniden kullanır (`@wwwroot/js/realtime-ui.js`).

---

## Yapılandırma (`appsettings.json`)

```json
"AI": {
  "Realtime": {
    "Enabled": true,
    "Model": "gpt-realtime-2",
    "ApiKey": "",
    "Voice": "alloy",
    "VadType": "semantic_vad",
    "VadEagerness": "medium",
    "TranscriptionModel": "gpt-4o-transcribe",
    "TranscriptionLanguage": "tr",
    "TranscriptionPrompt": "Müşteri destek görüşmesi. Sipariş numarası (ORD-1...), müşteri kodu (CUST-1990...), ürün adları..."
  }
}
```

**Alan açıklamaları**:

| Alan | Varsayılan | Açıklama |
|---|---|---|
| `Enabled` | `true` | Mod kapalıysa `/chat/realtime` endpoint 404 döner |
| `Model` | `gpt-realtime-2` | Realtime session modeli (STT+TTS session'ını yöneten ana model) |
| `ApiKey` | boş → `AI:OpenAI:ApiKey` fallback | Realtime-spesifik key opsiyonel; default OpenAI key'i kullanılır |
| `Voice` | `alloy` | TTS sesi: `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer` |
| `VadType` | `semantic_vad` | VAD tipi: `semantic_vad` (önerilen) veya `server_vad` (eski) |
| `VadEagerness` | `medium` | `semantic_vad` için konuşma sonu algılama: `low`, `medium`, `high` |
| `TranscriptionModel` | `gpt-4o-transcribe` | STT modeli. Alt seçenekler: `gpt-4o-mini-transcribe` (hızlı/ucuz), `whisper-1` (eski) |
| `TranscriptionLanguage` | `tr` | ISO-639-1 dil ipucu (null = otomatik) |
| `TranscriptionPrompt` | (domain örnekleri) | Sık geçen özel isim/kod formatlarını ASR'a önceden tanıtır → doğruluk artar |

**Not:** `gpt-realtime-2` API şeması `gpt-realtime-1.5`'ten farklıdır:
- `voice`, `input_audio_format`, `output_audio_format` artık **flat** değil; `session.audio.input/output` içinde **nested**.
- `max_response_output_tokens` parametresi kaldırıldı.
- `silence_duration_ms` yerine `semantic_vad` ile `eagerness` kullanılır.
- Event isimleri değişti: `audio.delta` → `response.output_audio.delta`, `transcript.delta` → `response.output_audio_transcript.delta`. |

### Transcription modeli neden Realtime modeliyle aynı değil?

OpenAI Realtime API'da iki ayrı model alanı vardır:

1. **Session modeli** (`Model`) — WebSocket oturumunu yöneten ana multimodal model (ses TTS'i bu model yapar).
2. **Transcription modeli** (`TranscriptionModel`) — `session.audio.input.transcription.model`. Kullanıcının sesini metne çeviren **ayrı** STT modeli. Buraya sadece STT-spesifik modeller konulabilir (`whisper-1`, `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`). `gpt-realtime-2` buraya konulamaz.

Bu ayrım sayesinde TTS kalitesini korurken STT'yi ayrıca ayarlayabilirsin (ör. maliyet için `gpt-4o-mini-transcribe`, maksimum doğruluk için `gpt-4o-transcribe`).

---

## Stream paylaşımı — text chat ile ortak UI

Sesli modun en önemli mimari kararı: **ikinci bir UI yazmadık**. Text chat'in streaming UI boru hattı (reasoning panel, agent chip, token-by-token bubble) aynen yeniden kullanıldı.

**Nasıl?** Backend event sözleşmesi aynı kılındı:

```csharp
// RealtimeBridge.HandleUserTranscriptAsync
await foreach (var evt in _reasoningService.ReasonStreamingAsync(...))
    await ForwardStreamEventAsync(browserWs, evt, ct);

await foreach (var evt in _team.RunStreamingAsync(...))
    await ForwardStreamEventAsync(browserWs, evt, ct);
```

`ForwardStreamEventAsync` bir `StreamEvent`'i `{type, data}` JSON olarak WS'e yazar — SSE formatının **WebSocket karşılığı**. Frontend (`realtime-ui.js`):

```js
user_transcript: ({ text }) => {
    app.ui.addMessage('user', text);
    streamCtx = app.ui.startStreamingMessage();       // boş bot iskeleti
},
chat_event: (evt) => {
    app._handleStreamEvent(streamCtx, reasoningState, evt);  // text chat handler'ı
},
workflow_done: () => app.ui.finalizeStreamingMessage(streamCtx),
```

Faydası: yeni bir event tipi veya görsel öğe (ör. sentiment panel) text chat'e eklendiğinde **sesli modda otomatik çalışır**, ayrıca yazmaya gerek yok.

---

## Durdurma kontrolleri

Frontend iki durdurma yolu sunar:

### 1) Tam kapatma — mikrofon butonu (kırmızı toggle)

```js
voiceBtn.addEventListener('click', () => {
    if (!client) start();
    else stop();
});
```

`RealtimeClient.stop()`:
- `{type:"stop"}` gönderir
- AudioWorklet disconnect + mikrofon track'larını release eder
- Playback AudioContext'i kapatır
- WS'i kapatır
- `beforeunload` event'inde **otomatik** tetiklenir

Backend tarafı (`RealtimeBridge.HandleBrowserControlAsync`) 1 sn timeout'lu kendi `CancellationTokenSource`'u ile `CloseAsync` çağırır — `RequestAborted` iptal edilmiş olsa bile graceful close yapar. `OperationCanceledException` ve `WebSocketException` sessizce yutulur.

### 2) Kısa kesinti — ⏸ interrupt butonu (bubble içinde)

```js
interrupt() {
    this._stopPlayback();                              // playback anında susar
    if (this.state === 'speaking') {
        this._sendControl({ type: 'interrupt' });      // sadece konuşurken
    }
}
```

Backend `response.cancel` ile OpenAI'ye iletir. `state !== 'speaking'` ise WS'e mesaj gönderilmez → "no active response" uyarısı log'larda oluşmaz.

---

## Güvenlik ve HITL entegrasyonu

Sesli mod **aynı güvenlik katmanlarından** geçer:

- **Input guard** (`IInputGuardrailService`): prompt injection, PII sızdırma, sanitize/reject kararı. Reject durumunda kullanıcıya sesli uyarı söyletilir.
- **HITL approval gate** (`ApprovalGateService`): `OrderPlacementAgent` veya `ComplaintAgent` yan-etkili tool çağırırsa admin onayı bekler. Sesli modda da tool **onay olmadan çalışmaz**. Admin panelinde onay/red UX'i aynıdır.
- **Live takeover**: Admin session'ı devralırsa `human_joined` event'i **hem text hem sesli** modda UI'yi günceller, bot akışı durur. Sesli mod için gelecekte "mikrofon otomatik kapansın" eklenebilir (şu an manuel).
- **Session persistence**: Transcript + bot yanıtı `chat.messages` ve `chat.bridge_messages` tablolarına yazılır — text chat ile **aynı** geçmiş görünür.

---

## OpenTelemetry ve trace

Sesli mod trace'leri text chat ile aynı `CustomerSupportBot` activity source altında akar:

- `ReasoningService.ReasonStreamingAsync` → `ai.reasoning` span'ı
- `CustomerSupportTeam.RunStreamingAsync` → `agent.run <AgentName>` span'ları (MAF'ın `UseOpenTelemetry` middleware'i — bkz. [agents.md#agent-level-opentelemetry](agents.md#agent-level-opentelemetry))
- Tool çağrıları → `tool.invoke <toolname>`
- `reasoning_traces` DB tablosu aynı şekilde populate olur, admin panelinde trace replay çalışır

Yani sesli bir konuşmanın her ajan adımını admin panelden **adım-adım** inceleyebilirsin (Trace Replay UI).

---

## Endpoint'ler

```
WebSocket: /chat/realtime/{sessionId?}         ← Köprü modu (RealtimeBridge + agent pipeline)
WebSocket: /chat/realtime-native/{sessionId?}  ← Native modu (RealtimeNativeBridge + tool calling)
```

- `sessionId` verilmezse yeni session oluşturulur; `connected` event'iyle ID döner
- Aynı session text chat ile paylaşılabilir — kullanıcı önce yazılı başlayıp sonra sesliye geçebilir

Handler: `@Services/Realtime/RealtimeBridge.cs:HandleAsync`. DI: `@Extensions/AiExtensions.cs` içinde `AddRealtimeServices(...)`.

---

## Sorun giderme

| Belirti | Muhtemel sebep | Çözüm |
|---|---|---|
| Mikrofon izni isteniyor ama ses gitmiyor | AudioWorklet load hatası | Console'a bak, `realtime-pcm-worklet.js` 404 mu? |
| Transcript boş veya saçma | Whisper-1 fallback, dil ipucu yok | `TranscriptionModel: gpt-4o-transcribe`, `TranscriptionLanguage: tr` |
| Konuşma erken kesiliyor | VAD çok agresif | `VadEagerness: low` veya `server_vad` kullan |
| Gürültü "konuşma" sanılıyor | VAD algılaması hassas | `semantic_vad` ile `eagerness: high` veya `server_vad` ile `threshold: 0.7` |
| Chip / reasoning panel görünmüyor | `window.chatApp` undefined | `app.js` init sonunda `window.chatApp = app` satırı olmalı; hard refresh gerek |
| `Cancellation failed: no active response` log'u | Interrupt boşa tetikleniyor | Frontend `state !== 'speaking'` koruması aktif mi? |
| `TaskCanceledException` stop sırasında | `CloseAsync` iptal edilmiş CT kullanıyor | `HandleBrowserControlAsync` 1 sn timeout'lu yeni CTS kullanır, güncel mi? |
| Asistan yanıt vermiyor | `create_response: true` kalmış | `session.update`'te kesin `false` olmalı — model bize cevap yetkisini devretmiş |
| `Unknown parameter: 'session.voice'` | Flat şema kullanılıyor | `session.audio.output.voice` içine alınmalı (nested audio şeması) |
| `Unknown parameter: 'session.max_response_output_tokens'` | gpt-realtime-2'de desteklenmiyor | Bu alanı `session.update` payload'undan kaldır |
| `Missing required parameter: 'session.audio.output.format.rate'` | PCM rate eksik | `session.audio.output.format` içine `rate: 24000` ekle |
| `Invalid modalities: ['audio', 'text']` | Birleşik modalite desteklenmiyor | `output_modalities: ["audio"]` yap (sadece tek mod) |
| `Invalid value: 'text'` | Assistant content type eski | `type: "output_text"` kullan ("text" yerine) |
| UI'da AI cevabı user sorusunun üstünde | gpt-realtime-2 hızlı başlıyor | Backend `assistant_text_delta` tamponlama yapıyor — bu normal, `user_transcript` geldikten sonra flush edilir |
| `model=gpt-realtime-1.5` loglanıyor | `appsettings.Development.json` override ediyor | `AI:Realtime:Model` değerini `gpt-realtime-2` yap veya kaldır (default'a düşsün) |

---

## İlgili dosyalar

**Backend**
- `@Services/Realtime/RealtimeBridge.cs` — köprü modu WS bridge + pump'lar + agent dispatcher
- `@Services/Realtime/RealtimeNativeBridge.cs` — native modu WS bridge + function call dispatch + TTS
- `@Services/Realtime/RealtimeFunctionTools.cs` — native modun okuma-only tool subset'i (5 fonksiyon — end_conversation dahil)
- `@Models/AiProviderOptions.cs` — `RealtimeOptions` POCO
- `@Extensions/ApplicationServicesExtensions.cs` — DI kayıtları (`RealtimeBridge`, `RealtimeNativeBridge`, `RealtimeFunctionTools`)
- `@Endpoints/RealtimeEndpoints.cs` — `/chat/realtime` ve `/chat/realtime-native` handler'ları

**Frontend**
- `@wwwroot/js/realtime-client.js` — WS client + AudioWorklet capture + PCM playback (her iki mod için ortak)
- `@wwwroot/js/realtime-ui.js` — iki mod arasında geçiş + tool call chip'leri (native) + ortak UI (köprü)
- `@wwwroot/js/realtime-pcm-worklet.js` — AudioWorklet processor (24kHz capture)
- `@wwwroot/index.html` — 🎤 ve ⚡ butonları
- `@wwwroot/css/styles.css` — `.btn-voice` (kırmızı pulse) + `.btn-voice-native` (mor pulse)

---

## ⚡ Hızlı Sesli (native mod)

### Mimari farkı

Köprü modunda backend **transcript çıkar çıkmaz** agent pipeline'ını tetikler ve nihai metni Realtime'a TTS olarak gönderir. Native modda ise:

- `create_response: true` — model **kendisi** sesli yanıt üretir, beklemez
- `tools` listesi modele expose edilir — model **kendisi** function calling yapar
- Tool sonuçları `function_call_output` ile modele döndürülür → model devam eder ve sesli okur
- **Reasoning yok, ajan zinciri yok, HITL yok** — saf model + tool çağrısı

```
[Ses] → gpt-realtime-2 → kendisi anlar/karar verir
          ↓ (tool gerekirse)
        response.function_call_arguments.done
          ↓
        backend: RealtimeFunctionTools.DispatchAsync(name, args)
          ↓
        ToolResult JSON → conversation.item.create (function_call_output)
          ↓
        response.create → model devam eder ve **sesli yanıt** üretir
          ↓
        response.output_audio.delta + response.output_audio_transcript.delta
```

**Event İsimleri (gpt-realtime-2):**
| Eski (1.5) | Yeni (2) |
|---|---|
| `audio.delta` | `response.output_audio.delta` |
| `transcript.delta` | `response.output_audio_transcript.delta` |
| `input_audio_buffer.speech_started` | `input_audio_buffer.speech_started` (değişmedi) |
| `conversation.item.input_audio_transcription.completed` | `conversation.item.input_audio_transcription.completed` (değişmedi) |
| `response.done` | `response.done` (değişmedi) |

Latency tipik: **800ms-1.5sn** (köprüde 3-5sn). Token maliyeti **~70% daha düşük** (tek model, zincirleme yok).

### Tool subset — yalnızca okuma-only

`@Services/Realtime/RealtimeFunctionTools.cs` modele **sadece** şu tool'ları tanıtır:

| Tool | Wrapper | Yan etki |
|---|---|---|
| `product_inquiry_tool` | `CustomerSupportTools.ProductInquiryTool(productName)` | Yok |
| `order_status_tool` | `CustomerSupportTools.OrderStatusTool(orderId)` | Yok |
| `get_last_order_tool` | `CustomerSupportTools.GetLastOrderTool(customerId)` | Yok |
| `get_all_orders_tool` | `CustomerSupportTools.GetAllOrdersTool(customerId)` | Yok |
| `end_conversation` | Native mod özel — görüşmeyi sonlandırma | Yok |

**Bilinçli olarak YOK** (model bu tanımları görmez):
- `order_placement_tool` — sipariş oluşturma yan etkili, HITL gerek
- `complaint_registration_tool` — şikayet kaydı yan etkili, HITL gerek
- `human_handoff_tool` — eskalasyon zinciri yan etkili

**end_conversation tool'u:**
Kullanıcı açıkça vedalaştığında (`"görüşürüz"`, `"hoşçakal"`, `"teşekkürler kapat"` vb.) model önce kısa bir veda cümlesi söyler, ardından bu tool'u çağırır. Backend:
1. Tool çağrısını algılar (`_endRequested = true`, `_endReason` alır)
2. `response.done` sonrası `{ type:"conversation_ended", reason }` browser'a gönderir
3. OpenAI ve browser WebSocket'lerini nazikçe kapatır (NormalClosure)

UI: `conversation_ended` callback'i ile "Görüşme sonlandırıldı" mesajı gösterilir, buton `idle`'a döner.

**Defense-in-depth**: model yine de bu isimlerden birini çağırırsa `RealtimeFunctionTools.DispatchAsync` `FORBIDDEN_IN_VOICE` hatası döner — ama pratikte sistem prompt'u modelin bu yola gitmesini engeller.

### Sistem prompt'u (model talimatı)

`@Services/Realtime/RealtimeNativeBridge.cs:BuildSystemInstructions` modele şu kuralları verir:

- **YAPABİLDİKLERİN**: ürün katalog sorgusu, sipariş durumu, son sipariş, müşterinin tüm siparişleri
- **YAPAMADIKLARIN** (tool ÇAĞIRMA, kullanıcıyı yazılı sohbete yönlendir):
  - YENİ SİPARİŞ OLUŞTURMA
  - ŞİKAYET KAYDI OLUŞTURMA
  - İADE / İPTAL / ÖDEME / HESAP işlemleri
- Tool sonuçlarını **yorumla**, ham JSON okuma (`status:"shipped"` → "kargoya verildi")
- Türkçe, kısa, sesli okumaya uygun (1-2 cümle)
- **GÖRÜŞMEYİ SONLANDIRMA**: Kullanıcı vedalaştığında (`"görüşürüz"`, `"hoşçakal"` vb.) önce veda cümlesi söyle, **ardından** `end_conversation` tool'unu çağır. Kullanıcı açıkça vedalaşmadıkça çağırma.

Kullanıcı yan-etkili bir şey isterse model şu kalıbı kullanır:
> "Bu işlemler güvenlik adımları gerektirdiği için yazılı sohbet üzerinden ilerletmeniz gerekiyor. Lütfen sohbet penceresine geçin, ben oradan da yardımcı olmaya devam edebilirim."

### Frontend UX farkı

| Öğe | Köprü modu | Native modu |
|---|---|---|
| Buton | 🎤 Mikrofon (kırmızı pulse) | ⚡ Şimşek (mor pulse) |
| Reasoning paneli | ✅ Var (intent, steps, entities, sentiment) | ❌ Yok |
| Agent chip | ✅ Birden fazla ("Planlama Ajanı" → "Sipariş Ajanı" → "Yanıt Ajanı") | ✅ Tek (`Ürün sorgulanıyor` gibi friendly tool adı) |
| Token-by-token altyazı | ✅ `appendResponseChunk` | ✅ `appendResponseChunk` (TTS ile eşzamanlı) |
| Markdown render | ✅ Final | ✅ Final |

`@wwwroot/js/realtime-ui.js` iki butonu **mutually exclusive** yönetir; biri aktifse diğerine basınca otomatik durdurup yeni mod başlatılır.

### Native mod event kontratı (browser ↔ backend)

Köprünün event sözleşmesinden **fark** olan event'ler:

| Event | Yön | Anlam |
|---|---|---|
| `connected` | ← | `{sessionId, mode:"native", model, voice, tools:[]}` — açılan tool'lar listesi |
| `tool_call` | ← | Model bir tool çağırdı: `{name, arguments}` (UI: chip göster) |
| `tool_result` | ← | Tool sonucu modele iletildi: `{name, output}` (UI: chip ✓) |
| `user_transcript` | ← | Kullanıcı transcript'i hazır (UI'da user bubble) |
| `assistant_text_delta` | ← | TTS ile **eşzamanlı** altyazı delta'sı (user_transcript'ten sonra) |
| `response_done` | ← | Model turunu bitirdi, yeni tura hazır |
| `conversation_ended` | ← | `{reason}` — görüşme sonlandırıldı (tool veya 60sn idle timeout) |

**UI Mesaj Sırası Düzeltmesi (Buffering):**
`gpt-realtime-2`'de `semantic_vad` kullanıldığında, model kullanıcı transcript'i tamamlanmadan **önce** yanıt üretmeye başlar. Bu nedenle backend `user_transcript` gelene kadar `assistant_text_delta`'ları **tamponlar** (buffer). `user_transcript` geldikten sonra biriken delta'lar UI'a flush edilir. Bu sayede UI'da kullanıcı mesajı → asistan mesajı sırası korunur. Audio akışı gerçek-zamanlı devam eder (tamponlanmaz).

Köprüye özgü `workflow_start`/`reasoning_*`/`agent`/`workflow_done` event'leri native modda **gönderilmez**.

### Persistence ve audit

Native modda **DB session geçmişine** asistan yanıtı yazılır (`@Services/Realtime/RealtimeNativeBridge.cs:HandleOpenAiEventAsync` → `_chatBridge.RecordBotExchange`), kullanıcı transcript'i ise `"(sesli)"` etiketiyle kaydedilir. Reasoning trace **yoktur** — bu modun ayırt edici farkı.

Bu trade-off bilinçlidir: hız ve maliyet için audit trail kısalır. Production'da audit kritikse bu kanal yalnızca okuma-only kalmalıdır (zaten kalır).

### Inactivity Timeout (Otomatik Sonlandırma)

Backend, son kullanıcı aktivitesini (audio chunk veya transcript) izler. **60 saniye** boyunca aktivite yoksa:
1. `{ type:"conversation_ended", reason:"idle_timeout" }` browser'a gönderilir
2. WebSocket'ler nazikçe kapatılır
3. UI'da "Görüşme sessizlik nedeniyle sonlandırıldı" mesajı gösterilir

Bu özellik açık unutulmuş mikrofonları önler. Aktivite zaman damgası `_lastUserActivityTicks` alanında atomik olarak tutulur (`Interlocked` operations).

---

## İlişkili dokümanlar

- [agents.md](agents.md) — Ajan takımı ve agent-level OpenTelemetry
- [workflow.md](workflow.md) — MAF workflow akışı (köprü modunda aynı)
- [patterns.md](patterns.md) — Streaming, HITL, admin takeover pattern'leri
- [runtime.md](runtime.md) — Servis yaşam döngüleri ve DI
