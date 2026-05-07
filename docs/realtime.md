# Sesli Konuşma Modu (Realtime)

OpenAI **Realtime API** (`gpt-realtime-1.5`) üzerinden kurulan full-duplex ses köprüsü. Kullanıcının sesi tarayıcıdan WebSocket ile backend'e, oradan da OpenAI Realtime'a akar; transcript çıkar çıkmaz **text chat ile aynı agent pipeline'ı** (reasoning → 7 ajanlı MAF workflow → response) tetiklenir, nihai metin tekrar Realtime API'ye gönderilip TTS olarak çalınır.

**Kritik tasarım kararı**: Realtime API **"köprü"** olarak kullanılır, beyin olarak değil. Modelin kendi konuşma yanıt verme yeteneği (`create_response: false`) kapatılmıştır; cevabı **her zaman** bizim agent takımımız üretir. Böylece sesli ve yazılı modda **aynı** HITL, reasoning, tool routing, semantic memory ve admin takeover davranışları çalışır.

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
   - session.update ile: voice, VAD, transcription config, create_response=false
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
    "Model": "gpt-realtime-1.5",
    "ApiKey": "",
    "Voice": "alloy",
    "VadSilenceMs": 600,
    "VadThreshold": 0.5,
    "MaxResponseTokens": 4096,
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
| `Model` | `gpt-realtime-1.5` | Realtime session modeli (STT+TTS session'ını yöneten ana model) |
| `ApiKey` | boş → `AI:OpenAI:ApiKey` fallback | Realtime-spesifik key opsiyonel; default OpenAI key'i kullanılır |
| `Voice` | `alloy` | TTS sesi: `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer` |
| `VadSilenceMs` | 600 | Kullanıcı konuşmayı bitirdi kabul etme eşiği. 800-1000 ms Türkçe uzun cümleler için daha rahat |
| `VadThreshold` | 0.5 | Ses algılama eşiği (0..1). Gürültülü ortamda 0.55-0.6'ya çıkar |
| `MaxResponseTokens` | 4096 | TTS sırasında aşılmaz |
| `TranscriptionModel` | `gpt-4o-transcribe` | STT modeli. Alt seçenekler: `gpt-4o-mini-transcribe` (hızlı/ucuz), `whisper-1` (eski) |
| `TranscriptionLanguage` | `tr` | ISO-639-1 dil ipucu (null = otomatik) |
| `TranscriptionPrompt` | (domain örnekleri) | Sık geçen özel isim/kod formatlarını ASR'a önceden tanıtır → doğruluk artar |

### Transcription modeli neden Realtime modeliyle aynı değil?

OpenAI Realtime API'da iki ayrı model alanı vardır:

1. **Session modeli** (`Model`) — WebSocket oturumunu yöneten ana multimodal model (ses TTS'i bu model yapar).
2. **Transcription modeli** (`TranscriptionModel`) — `session.input_audio_transcription.model`. Kullanıcının sesini metne çeviren **ayrı** STT modeli. Buraya sadece STT-spesifik modeller konulabilir (`whisper-1`, `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`). `gpt-realtime-1.5` buraya konulamaz.

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

## Endpoint

```
WebSocket: /chat/realtime?sessionId={optional-existing-session}
```

- `sessionId` verilmezse yeni session oluşturulur; `connected` event'iyle ID döner
- Aynı session text chat ile paylaşılabilir — kullanıcı önce yazılı başlayıp sonra sesliye geçebilir

Handler: `@Services/Realtime/RealtimeBridge.cs:HandleAsync`. DI: `@Extensions/AiExtensions.cs` içinde `AddRealtimeServices(...)`.

---

## Sorun giderme

| Belirti | Muhtemel sebep | Çözüm |
|---|---|---|
| Mikrofon izni isteniyor ama ses gitmiyor | AudioWorklet load hatası | Console'a bak, `pcm-processor.js` 404 mu? |
| Transcript boş veya saçma | Whisper-1 fallback, dil ipucu yok | `TranscriptionModel: gpt-4o-transcribe`, `TranscriptionLanguage: tr` |
| Konuşma erken kesiliyor | VAD çok agresif | `VadSilenceMs: 800-1000`, `VadThreshold: 0.55` |
| Gürültü "konuşma" sanılıyor | VAD threshold düşük | `VadThreshold: 0.6` |
| Chip / reasoning panel görünmüyor | `window.chatApp` undefined | `app.js` init sonunda `window.chatApp = app` satırı olmalı; hard refresh gerek |
| `Cancellation failed: no active response` log'u | Interrupt boşa tetikleniyor | Frontend `state !== 'speaking'` koruması aktif mi? |
| `TaskCanceledException` stop sırasında | `CloseAsync` iptal edilmiş CT kullanıyor | `HandleBrowserControlAsync` 1 sn timeout'lu yeni CTS kullanır, güncel mi? |
| Asistan yanıt vermiyor | `create_response: true` kalmış | `session.update`'te kesin `false` olmalı — model bize cevap yetkisini devretmiş |

---

## İlgili dosyalar

- `@Services/Realtime/RealtimeBridge.cs` — backend WS bridge + pump'lar + agent dispatcher
- `@Models/AiProviderOptions.cs` — `RealtimeOptions` POCO
- `@Extensions/AiExtensions.cs` — DI kayıtları
- `@Endpoints/RealtimeEndpoints.cs` — `/chat/realtime` handler
- `@wwwroot/js/realtime-client.js` — browser WS client + AudioWorklet capture + PCM playback
- `@wwwroot/js/realtime-ui.js` — text chat UI'sine köprü + durdurma kontrolleri
- `@wwwroot/js/pcm-processor.js` — AudioWorklet processor (24kHz capture)

---

## İlişkili dokümanlar

- [agents.md](agents.md) — Ajan takımı ve agent-level OpenTelemetry
- [workflow.md](workflow.md) — MAF workflow akışı (sesli modda da aynı)
- [patterns.md](patterns.md) — Streaming, HITL, admin takeover pattern'leri
- [runtime.md](runtime.md) — Servis yaşam döngüleri ve DI
