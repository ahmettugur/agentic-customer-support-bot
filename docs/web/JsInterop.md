# JavaScript Interop

**Klasör:** `wwwroot/js/`

Blazor WASM'in JS dünyasıyla köprüsü. Bazı işler (audio, SSE fetch, DOM manipulation) JS'te daha kolay/performanslı.

| Dosya | Sorumluluk |
|---|---|
| `chat-bridge.js` | Chat sayfası SSE + voice integration shim'i |
| `admin-chat-bridge.js` | Admin paneli SSE bağlantısı |
| `realtime-client.js` | Voice WebSocket + AudioContext |
| `realtime-pcm-worklet.js` | AudioWorklet (Float32 → PCM16 dönüşümü) |
| `realtime-ui.js` | Voice UI controller (buton, status, mod seçim) |
| `sla-bridge.js` | SLA SSE (opsiyonel, default kullanılmıyor) |

---

## Blazor ↔ JS Interop pattern

### JS → C# (DotNetObjectReference)

```csharp
// Blazor component
private DotNetObjectReference<Chat>? _dotNetRef;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("__chatSetup", _dotNetRef, _apiBaseUrl);
    }
}

[JSInvokable]
public Task OnStreamEvent(string type, string data)
{
    // JS bu metodu çağırır
    return Task.CompletedTask;
}

public void Dispose() => _dotNetRef?.Dispose();
```

```javascript
// JS
window.__chatSetup = (dotNetRef, apiBase) => {
    window.__chatRef = dotNetRef;
    window.__chatApiBase = apiBase;
};

// C# metoduna çağrı
await dotNetRef.invokeMethodAsync('OnStreamEvent', 'reasoning_start', JSON.stringify(data));
```

### C# → JS

```csharp
// Void method
await JS.InvokeVoidAsync("__streamChat", _dotNetRef, _apiBaseUrl, text, sessionId);

// Return value
var result = await JS.InvokeAsync<string>("localStorage.getItem", "cs.auth");
```

---

## chat-bridge.js

Minimal Blazor↔JS shim'i.

```javascript
// Setup — Blazor ref ve API base'i kaydet
window.__chatSetup = (dotNetRef, apiBase) => {
    window.__chatRef = dotNetRef;
    window.__chatApiBase = apiBase;
};

// Mesaj listesini en alta scroll et (her render'da)
window.__scrollToBottom = (id) => {
    requestAnimationFrame(() => {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    });
};

// SSE fetch — /chat/stream
window.__streamChat = async (dotNetRef, apiBase, text, sessionId) => {
    try
    {
        const resp = await fetch(`${apiBase}/chat/stream`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
            body: JSON.stringify({ sessionId, message: text })
        });

        const reader = resp.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // SSE block: "event: TYPE\ndata: JSON\n\n"
            let idx;
            while ((idx = buffer.indexOf('\n\n')) !== -1) {
                const block = buffer.slice(0, idx).trim();
                buffer = buffer.slice(idx + 2);

                const lines = block.split('\n');
                let eventType = 'message';
                let data = '';
                for (const line of lines) {
                    if (line.startsWith('event:')) eventType = line.slice(6).trim();
                    else if (line.startsWith('data:')) data += line.slice(5).trim();
                }

                if (data) {
                    await dotNetRef.invokeMethodAsync('OnStreamEvent', eventType, data);
                }
            }
        }

        await dotNetRef.invokeMethodAsync('OnStreamComplete');
    } catch (err) {
        console.error('[ChatStream]', err);
        await dotNetRef.invokeMethodAsync('OnStreamEvent', 'error', err.message);
    }
};
```

### Persistent events — `/chat/events/{sessionId}`

```javascript
window.__startPersistentEvents = (dotNetRef, apiBase, sessionId, accessToken) => {
    const url = `${apiBase}/chat/events/${sessionId}` +
                (accessToken ? `?access_token=${accessToken}` : '');
    const es = new EventSource(url);

    es.addEventListener('humanJoined', e => {
        dotNetRef.invokeMethodAsync('OnPersistentEvent', 'human_joined', e.data);
    });
    es.addEventListener('humanLeft', e => {
        dotNetRef.invokeMethodAsync('OnPersistentEvent', 'human_left', e.data);
    });
    es.addEventListener('handoffPending', e => {
        dotNetRef.invokeMethodAsync('OnPersistentEvent', 'handoff_pending', e.data);
    });
    es.addEventListener('humanMessage', e => {
        dotNetRef.invokeMethodAsync('OnPersistentEvent', 'human_message', e.data);
    });

    window.__persistentEventSource = es;
};

window.__stopPersistentEvents = () => {
    window.__persistentEventSource?.close();
    window.__persistentEventSource = null;
};
```

EventSource browser API'sı — SSE için optimize. Auto-reconnect, retry built-in.

### window.App & window.chatApp shim'leri

`realtime-ui.js` Blazor olmayan bir UI bekler. Bu shim'ler bridge görevi görür:

```javascript
window.App = {
    sendMessage: (text) => window.__chatRef?.invokeMethodAsync('VoiceSendMessage', text),
    newChat: () => window.__chatRef?.invokeMethodAsync('NewChatFromVoice'),
    voiceTranscript: (text) => window.__chatRef?.invokeMethodAsync('VoiceTranscript', text),
    voiceStreamEvent: (type, data) =>
        window.__chatRef?.invokeMethodAsync('OnStreamEvent', type, JSON.stringify(data)),
    voiceStreamComplete: () => window.__chatRef?.invokeMethodAsync('OnStreamComplete'),
};

window.chatApp = {
    api: {
        baseUrl: () => window.__chatApiBase,
        sessionId: () => window.__currentSessionId,
        setSession: (sid) => { window.__currentSessionId = sid; },
        resetSession: () => { window.__currentSessionId = null; },
    },
    ui: {
        addMessage: (role, text) => { /* DOM manipulation */ },
        startStreamingMessage: () => { /* DOM manipulation */ },
        appendResponseChunk: (ctx, text) => { /* DOM manipulation */ },
        setAgentStatus: (ctx, label, state) => { /* DOM manipulation */ },
        scrollToBottom: () => { window.__scrollToBottom('messages'); },
    }
};
```

---

## realtime-client.js — WebSocket Voice

```javascript
class RealtimeClient {
    constructor({ baseUrl, sessionId, callbacks, endpoint }) {
        this.baseUrl = baseUrl;
        this.sessionId = sessionId;
        this.callbacks = callbacks || {};
        this.endpoint = endpoint || '/chat/realtime';
        this.state = 'idle';
        this.ws = null;
        this.audioCtx = null;
        this.workletNode = null;
        this.micStream = null;
        this.playbackQueue = [];
    }

    async start() {
        this._setState('connecting');

        // 1. Mikrofon access
        this.micStream = await navigator.mediaDevices.getUserMedia({ audio: true });

        // 2. AudioContext + Worklet
        this.audioCtx = new AudioContext({ sampleRate: 24000 });
        await this.audioCtx.audioWorklet.addModule('/js/realtime-pcm-worklet.js');
        this.workletNode = new AudioWorkletNode(this.audioCtx, 'pcm-capture-processor');

        // Mic → Worklet
        const micSource = this.audioCtx.createMediaStreamSource(this.micStream);
        micSource.connect(this.workletNode);

        // Worklet → main thread (PCM16 ArrayBuffer)
        this.workletNode.port.onmessage = (e) => {
            if (this.ws?.readyState === WebSocket.OPEN && this.state === 'listening') {
                this.ws.send(e.data);   // Binary frame
            }
        };

        // 3. WebSocket bağlantısı
        const wsUrl = this.baseUrl.replace(/^http/, 'ws') + this.endpoint
                    + (this.sessionId ? `/${this.sessionId}` : '');
        this.ws = new WebSocket(wsUrl);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = () => {
            this._setState('listening');
            this.callbacks.connected?.({ sessionId: this.sessionId });
        };

        this.ws.onmessage = (e) => {
            if (e.data instanceof ArrayBuffer) {
                // PCM16 audio chunk → playback
                this._enqueueAudio(e.data);
                this._setState('speaking');
            } else {
                // JSON text event
                const msg = JSON.parse(e.data);
                this._handleJsonEvent(msg);
            }
        };

        this.ws.onerror = (err) => {
            this._setState('error');
            this.callbacks.error?.({ message: err.message || 'WebSocket error' });
        };

        this.ws.onclose = () => {
            this._setState('idle');
            this.callbacks.close?.();
        };
    }

    _handleJsonEvent(msg) {
        switch (msg.type) {
            case 'session.created':
                this.sessionId = msg.sessionId;
                this.callbacks.connected?.({ sessionId: msg.sessionId });
                break;
            case 'user_transcript':
                this.callbacks.user_transcript?.({ text: msg.text });
                break;
            case 'chat_event':
                this.callbacks.chat_event?.(msg);
                break;
            case 'workflow_done':
                this.callbacks.workflow_done?.();
                this._setState('listening');
                break;
            case 'response_done':
                this.callbacks.response_done?.();
                this._setState('listening');
                break;
            case 'error':
                this.callbacks.error?.(msg);
                break;
        }
    }

    _enqueueAudio(buffer) {
        // PCM16 24kHz → AudioBuffer
        const samples = new Int16Array(buffer);
        const float32 = new Float32Array(samples.length);
        for (let i = 0; i < samples.length; i++) {
            float32[i] = samples[i] / 32768.0;
        }

        const audioBuffer = this.audioCtx.createBuffer(1, float32.length, 24000);
        audioBuffer.copyToChannel(float32, 0);

        const source = this.audioCtx.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(this.audioCtx.destination);
        source.start();   // Hemen oynat
        this.playbackQueue.push(source);
    }

    interrupt() {
        // Asistan konuşmayı kes
        for (const src of this.playbackQueue) {
            try { src.stop(); } catch {}
        }
        this.playbackQueue = [];
        this.ws?.send(JSON.stringify({ type: 'interrupt' }));
        this._setState('listening');
    }

    stop() {
        this.workletNode?.disconnect();
        this.micStream?.getTracks().forEach(t => t.stop());
        this.audioCtx?.close();
        this.ws?.close();
        this._setState('idle');
    }

    _setState(s) {
        this.state = s;
        this.callbacks.state?.({ state: s });
    }
}

window.RealtimeClient = RealtimeClient;
```

### Önemli noktalar

| Detay | Açıklama |
|---|---|
| Sample rate | 24000 Hz (OpenAI Realtime ile uyumlu) |
| Format | PCM16 (signed int16 little-endian) |
| Worklet | Düşük latency, ses thread'inde çalışır (UI bloklamaz) |
| Binary frame | Mic → server: ArrayBuffer; Server → client: ArrayBuffer |
| Text frame | JSON event'ler |
| Playback queue | Çoklu chunk geldiğinde sıralı oynat |
| Interrupt | `response.cancel` event + local playback stop |

---

## realtime-pcm-worklet.js

AudioWorklet processor — main thread'den ayrı audio thread'de çalışır.

```javascript
class PcmCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this._buffer = new Int16Array(1200);   // ~50ms @ 24kHz
        this._idx = 0;
    }

    process(inputs) {
        const input = inputs[0];
        if (!input || !input[0]) return true;

        const channel = input[0];   // Float32, 128 samples typical

        for (let i = 0; i < channel.length; i++) {
            // Float32 [-1, 1] → Int16 [-32768, 32767]
            const s = Math.max(-1, Math.min(1, channel[i]));
            this._buffer[this._idx++] = s < 0 ? s * 0x8000 : s * 0x7FFF;

            if (this._idx >= this._buffer.length) {
                // Buffer dolu → main thread'e gönder
                const out = this._buffer.buffer;
                this.port.postMessage(out, [out]);   // Zero-copy transfer
                this._buffer = new Int16Array(1200);
                this._idx = 0;
            }
        }

        return true;
    }
}

registerProcessor('pcm-capture-processor', PcmCaptureProcessor);
```

### Zero-copy transfer

`postMessage(out, [out])` ikinci parametre — transfer list. `out` ArrayBuffer ownership main thread'e geçer (kopyalama yok). Worklet yeni buffer alır.

Bu sayede 24kHz × 16-bit = 48 KB/sn data transferi GC baskısı yapmaz.

### Frame boyutu neden 1200?

```
1200 samples / 24000 Hz = 50ms
```

50ms latency hem hız hem stabilite için iyi denge:
- Daha küçük: daha çok WebSocket overhead, latency aslında düşmez
- Daha büyük: hissedilebilir gecikme

OpenAI Realtime API server-side VAD ile uyumlu.

---

## realtime-ui.js

Voice UI controller — buton, status, mod seçim.

```javascript
window.__initVoiceUi = (apiBase, sessionId) => {
    const voiceBtn = document.getElementById('voiceBtn');
    const voiceNativeBtn = document.getElementById('voiceNativeBtn');
    const statusBar = document.getElementById('voiceStatus');
    const statusText = document.getElementById('voiceStatusText');
    const interruptBtn = document.getElementById('voiceInterruptBtn');

    let client = null;

    const startBridge = () => startVoice('/chat/realtime');
    const startNative = () => startVoice('/chat/realtime-native');

    const startVoice = (endpoint) => {
        if (client) { client.stop(); client = null; return; }

        client = new RealtimeClient({
            baseUrl: apiBase,
            sessionId,
            endpoint,
            callbacks: {
                state: ({ state }) => {
                    statusBar.hidden = state === 'idle';
                    statusText.textContent = LABELS[state] || state;
                },
                connected: ({ sessionId: newSid }) => {
                    if (newSid) window.chatApp.api.setSession(newSid);
                },
                user_transcript: ({ text }) => window.App.voiceTranscript(text),
                chat_event: (evt) => window.App.voiceStreamEvent(evt.type, evt.data),
                workflow_done: () => window.App.voiceStreamComplete(),
                error: ({ message }) => alert(`Voice hatası: ${message}`),
                close: () => { client = null; },
            }
        });

        client.start();
    };

    voiceBtn.addEventListener('click', startBridge);
    voiceNativeBtn.addEventListener('click', startNative);
    interruptBtn.addEventListener('click', () => client?.interrupt());
};

const LABELS = {
    idle: 'Sesli konuşma',
    connecting: 'Bağlanıyor…',
    listening: '🎤 Dinliyor',
    thinking: '💭 Düşünüyor',
    speaking: '🔊 Konuşuyor',
    error: 'Hata',
};
```

---

## admin-chat-bridge.js

Admin paneli "Active Chats" tab'ının canlı SSE bağlantısı.

```javascript
window.__adminChatSetup = (accessToken, apiBase) => {
    window.__adminToken = accessToken;
    window.__adminApiBase = apiBase;
};

window.__adminSubscribeChat = (dotNetRef, sessionId) => {
    // Eski subscription'ı kapat
    window.__adminStopChat();

    const url = `${window.__adminApiBase}/chat-sessions/${sessionId}/subscribe` +
                `?access_token=${window.__adminToken}`;
    const es = new EventSource(url);

    es.addEventListener('bridge_message', e => {
        dotNetRef.invokeMethodAsync('OnChatEvent', 'bridge_message', e.data);
    });

    es.onerror = (err) => {
        console.error('[AdminSSE]', err);
    };

    window.__adminEventSource = es;
};

window.__adminStopChat = () => {
    window.__adminEventSource?.close();
    window.__adminEventSource = null;
};
```

### Token query string ile

EventSource header gönderemez → `?access_token=...` query param ile. Server JWT middleware'i bunu yakalar (`AuthServicesExtensions.OnMessageReceived`).

---

## sla-bridge.js (opsiyonel)

```javascript
window.__slaSubscribe = (dotNetRef, accessToken) => {
    const url = `${apiBase}/sla/events?access_token=${accessToken}`;
    const es = new EventSource(url);

    es.addEventListener('sla_event', e => {
        dotNetRef.invokeMethodAsync('OnSlaEvent', e.data);
    });
};
```

`Sla.razor` polling kullanıyor — bu bridge default kullanılmıyor. SSE'ye geçilmek istenirse hazır.

---

## Lazy loading pattern

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;

    await JS.InvokeVoidAsync("loadScript", "/js/chat-bridge.js", "chat-bridge");
    await JS.InvokeVoidAsync("loadScript", "/js/realtime-client.js", "realtime-client");
}
```

`loadScript` Promise döner — script yüklenince resolve. `id` parametresi ile duplicate yükleme önlenir:

```javascript
window.loadScript = function(src, id) {
    return new Promise((resolve, reject) => {
        if (id && document.getElementById(id)) return resolve();   // Zaten var
        const s = document.createElement('script');
        s.src = src;
        if (id) s.id = id;
        s.onload = resolve;
        s.onerror = reject;
        document.head.appendChild(s);
    });
};
```

Bu sayede sadece **ihtiyaç olunca** JS yüklenir — admin sayfasını ziyaret eden user `realtime-client.js` (audio code) yüklemez.

---

## Memory leak prevention

```csharp
private DotNetObjectReference<Chat>? _dotNetRef;

public void Dispose()
{
    _dotNetRef?.Dispose();   // KRİTİK
}
```

`DotNetObjectReference` dispose edilmezse component GC'lenmez — page navigation'da memory leak.

`@implements IDisposable` veya `@implements IAsyncDisposable` Razor component'inde — Blazor framework `Dispose`'u otomatik çağırır.

---

## Bağlantılar

- [Pages-Chat.md](Pages-Chat.md) — chat-bridge.js + realtime kullanımı
- [Pages-Admin.md](Pages-Admin.md) — admin-chat-bridge.js
- [Api Endpoints-Chat](../api/Endpoints-Chat.md) — `/chat/stream`, `/chat/realtime` server tarafı
- [Adapters.AI Realtime](../adapters-ai/Realtime.md) — OpenAI Realtime API
- [Api Services ChatEventOrchestrator](../api/Services.md)
