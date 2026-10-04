// realtime-client.js
// Tarayıcı tarafı sesli görüşme istemcisi (backend /chat/realtime-native WebSocket'i).
//
// Akış:
//   getUserMedia → AudioWorklet (PCM16 24kHz) → WebSocket binary → backend
//   backend → WebSocket binary (TTS PCM16) → AudioBuffer → AudioContext.play
//   backend → WebSocket text (JSON) → callback'ler (transcript, status)
//
// Public API:
//   const client = new RealtimeClient({ baseUrl, sessionId, callbacks });
//   await client.start();
//   client.interrupt();
//   client.setMuted(true);
//   client.getLevels();   // { input, output } 0–1 — görüşme ekranındaki küre için
//   client.stop();

(function () {
    'use strict';

    const SAMPLE_RATE = 24000;

    class RealtimeClient {
        constructor({ baseUrl, sessionId, callbacks, endpoint }) {
            this.baseUrl = baseUrl || ''; // boş ise aynı origin
            this.sessionId = sessionId || null;
            this.callbacks = callbacks || {};
            this.endpoint = endpoint || '/chat/realtime-native';

            this.ws = null;
            this.mediaStream = null;
            this.audioCtx = null;
            this.workletNode = null;
            this.sourceNode = null;

            // Asistan TTS playback için ayrı context
            this.playCtx = null;
            this._playCursor = 0; // sıralı playback için zaman damgası

            this.state = 'idle'; // idle | connecting | listening | speaking | error
            this._disposed = false;
            this._micTrack = null;
            this._muted = false;       // kullanıcı sessize aldı — dinlemeye dönüşte de kapalı kalır
            this._inAnalyser = null;   // mikrofon seviyesi (küre)
            this._outAnalyser = null;  // asistan sesi seviyesi (küre)
            this._levelBuf = null;
        }

        // ─── Public ───

        async start() {
            if (this.state !== 'idle') return;
            this._setState('connecting');

            try {
                await this._initAudio();
                await this._connectWs();
            } catch (err) {
                console.error('Realtime start hatası:', err);
                this._setState('error');
                this._emit('error', { message: err.message || String(err) });
                this.stop();
                throw err;
            }
        }

        get muted() { return this._muted; }

        /** Kalıcı sessize alma: asistan konuşmayı bitirip dinlemeye dönüldüğünde de mikrofon kapalı kalır. */
        setMuted(muted) {
            this._muted = !!muted;
            if (this._micTrack) {
                try { this._micTrack.enabled = !this._muted && this.state !== 'speaking'; } catch { }
            }
            this._emit('muted', { muted: this._muted });
        }

        /** Kürenin anlık seviyeleri (0–1): mikrofon ve asistan sesi. Ses yoksa 0. */
        getLevels() {
            return { input: this._rms(this._inAnalyser), output: this._rms(this._outAnalyser) };
        }

        interrupt() {
            // Mikrofon gecikmeli açma timer'ını iptal et — kullanıcı müdahale etti.
            clearTimeout(this._micReopenTimer);
            this._stopPlayback();
            if (this.state === 'speaking') {
                this._sendControl({ type: 'interrupt' });
                this._setState('listening'); // interrupt'ta hemen aç
            }
        }

        stop() {
            // Dispose flag — stop() sonrası gelen geç WS mesajları (önceki turun
            // response.audio.delta'ları veya geciken transcript'leri) ignore edilir.
            // Mod geçişlerinde (köprü → native veya tersi) hayalet baloncukları önler.
            this._disposed = true;
            this._micTrack = null;
            this._inAnalyser = null;
            this._outAnalyser = null;

            try { this._sendControl({ type: 'stop' }); } catch { /* ignore */ }

            if (this.workletNode) {
                this.workletNode.port.onmessage = null;
                this.workletNode.disconnect();
                this.workletNode = null;
            }
            if (this.sourceNode) {
                try { this.sourceNode.disconnect(); } catch { }
                this.sourceNode = null;
            }
            if (this.mediaStream) {
                this.mediaStream.getTracks().forEach(t => t.stop());
                this.mediaStream = null;
            }
            if (this.audioCtx) {
                try { this.audioCtx.close(); } catch { }
                this.audioCtx = null;
            }
            if (this.playCtx) {
                try { this.playCtx.close(); } catch { }
                this.playCtx = null;
            }
            clearTimeout(this._micReopenTimer);
            if (this.ws) {
                try { this.ws.close(); } catch { }
                this.ws = null;
            }
            this._setState('idle');
        }

        // ─── Audio init ───

        async _initAudio() {
            // Mikrofon
            this.mediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    channelCount: 1,
                    sampleRate: SAMPLE_RATE,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });
            // Mikrofon track referansı — bot konuşurken `enabled=false` ile sessize
            // alıp hoparlör→mikrofon echo loop'unu kaynakında keseriz. Backend
            // half-duplex gating ile birlikte iki katmanlı koruma.
            this._micTrack = this.mediaStream.getAudioTracks()[0] || null;

            // Capture context — 24kHz
            this.audioCtx = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: SAMPLE_RATE
            });

            await this.audioCtx.audioWorklet.addModule('js/realtime-pcm-worklet.js');

            this.sourceNode = this.audioCtx.createMediaStreamSource(this.mediaStream);
            this.workletNode = new AudioWorkletNode(this.audioCtx, 'pcm-capture-processor');
            this.workletNode.port.onmessage = (e) => this._onAudioChunk(e.data);
            this.sourceNode.connect(this.workletNode);
            // Küre için mikrofon seviyesi — analizör hiçbir yere bağlanmaz (yalnızca ölçer).
            this._inAnalyser = this.audioCtx.createAnalyser();
            this._inAnalyser.fftSize = 512;
            this.sourceNode.connect(this._inAnalyser);
            // Worklet'i destination'a bağlamıyoruz — kullanıcı kendi sesini duymasın

            // Playback context (asistanı çalmak için) — aynı sample rate
            this.playCtx = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: SAMPLE_RATE
            });
            this._playCursor = this.playCtx.currentTime;
            // Asistan sesi tek bir analizörden geçip hoparlöre gider (küre seviyesi).
            this._outAnalyser = this.playCtx.createAnalyser();
            this._outAnalyser.fftSize = 512;
            this._outAnalyser.connect(this.playCtx.destination);
        }

        _onAudioChunk(arrayBuffer) {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
            this.ws.send(arrayBuffer);
        }

        // ─── WS ───

        async _connectWs() {
            const baseProto = this.baseUrl
                ? (/^https:/i.test(this.baseUrl) ? 'wss:' : 'ws:')
                : (location.protocol === 'https:' ? 'wss:' : 'ws:');
            const proto = baseProto;
            const host = this.baseUrl
                ? this.baseUrl.replace(/^https?:/i, proto)
                : proto + '//' + location.host;
            // WebSocket handshake'ine Authorization header'ı EKLENEMEZ (tarayıcı API'si izin
            // vermiyor), bu yüzden token query string'den geçer — sunucu tarafında zaten bu
            // mekanizma var (bkz. AuthServicesExtensions.cs OnMessageReceived, SSE için de
            // aynısı kullanılıyor). Token olmadan bağlantı 401 alır; onunla birlikte sesli
            // kanal da müşteriyi tanır ve sipariş sorguları çalışır.
            const tokenQs = window._authToken
                ? '?access_token=' + encodeURIComponent(window._authToken)
                : '';
            const url = host + this.endpoint + '/' + (this.sessionId || '') + tokenQs;

            this.ws = new WebSocket(url);
            this.ws.binaryType = 'arraybuffer';

            this.ws.onopen = () => { /* sunucu connected event'i gönderecek */ };
            this.ws.onmessage = (e) => this._onWsMessage(e);
            this.ws.onerror = (e) => {
                console.error('Realtime WS error', e);
                this._setState('error');
                this._emit('error', { message: 'WebSocket bağlantı hatası' });
            };
            this.ws.onclose = () => {
                if (this.state !== 'idle' && this.state !== 'error') {
                    this._setState('idle');
                    this._emit('close', {});
                }
            };
        }

        _onWsMessage(e) {
            // stop() çağrılmışsa kalan tampon mesajları işleme — hayalet event'leri önler.
            if (this._disposed) return;

            if (typeof e.data === 'string') {
                let msg;
                try { msg = JSON.parse(e.data); } catch { return; }
                this._handleControl(msg);
            } else if (e.data instanceof ArrayBuffer) {
                this._enqueueAudio(e.data);
            } else if (e.data instanceof Blob) {
                e.data.arrayBuffer().then(buf => { if (!this._disposed) this._enqueueAudio(buf); });
            }
        }

        _handleControl(msg) {
            switch (msg.type) {
                case 'connected':
                    this.sessionId = msg.sessionId || this.sessionId;
                    this._setState('listening');
                    this._emit('connected', msg);
                    break;
                case 'speech_started':
                    this._emit('speech_started', msg);
                    break;
                case 'speech_stopped':
                    this._emit('speech_stopped', msg);
                    break;
                case 'user_transcript':
                    this._emit('user_transcript', msg);
                    break;
                case 'user_transcript_delta':
                    // Canlı altyazı — kullanıcı konuşurken gelen transkript parçası (yalnızca ekran)
                    this._emit('user_transcript_delta', msg);
                    break;
                case 'assistant_text':
                    this._setState('speaking');
                    this._emit('assistant_text', msg);
                    break;
                case 'assistant_text_delta':
                    this._emit('assistant_text_delta', msg);
                    break;
                case 'response_done':
                    // TTS ses chunk'ları AudioContext kuyruğunda henüz çalınmamış
                    // olabilir. Mikrofonu hemen açmak hoparlör→mikrofon echo'ya yol
                    // açar ve OpenAI bunu kullanıcı sesi sanır (sahte user_transcript).
                    // Kuyruktaki sesin bitmesini bekleyip sonra açıyoruz.
                    this._scheduleListeningTransition();
                    this._emit('response_done', msg);
                    break;
                case 'tool_call':
                    // Model bir tool çağırdı (UI ipucu)
                    this._emit('tool_call', msg);
                    break;
                case 'tool_result':
                    // Tool sonucu modele iletildi (UI ipucu)
                    this._emit('tool_result', msg);
                    break;
                case 'conversation_ended':
                    // Backend görüşmeyi nazikçe sonlandırdı (end_conversation tool veya idle timeout)
                    this._emit('conversation_ended', msg);
                    this._setState('idle');
                    // stop() WS'i kapatır + mic track'leri durdurur
                    try { this.stop(); } catch (_) { /* best effort */ }
                    break;
                case 'error':
                    this._setState('error');
                    this._emit('error', msg);
                    break;
                default:
                    // Yazılı sohbet SSE sözleşmesiyle aynı biçimdeki ({type, data}) diğer olaylar
                    // (ör. sentiment_update / sentiment_alert) — UI'ya ham olarak yansıtılır.
                    this._emit('chat_event', { type: msg.type, data: msg.data });
                    break;
            }
        }

        _sendControl(payload) {
            if (this.ws && this.ws.readyState === WebSocket.OPEN) {
                this.ws.send(JSON.stringify(payload));
            }
        }

        // ─── Playback ───

        _enqueueAudio(arrayBuffer) {
            if (!this.playCtx || arrayBuffer.byteLength === 0) return;

            // İlk ses chunk'ı geldiğinde state'i speaking'e geçir → mic track gating
            // tetiklensin. Native modda backend ayrı bir 'speaking_started' eventi
            // göndermediği için bu otomatik geçiş zorunlu.
            if (this.state !== 'speaking') {
                this._setState('speaking');
            }

            // PCM16 LE -> Float32 [-1,1]
            const view = new DataView(arrayBuffer);
            const sampleCount = arrayBuffer.byteLength / 2;
            const buffer = this.playCtx.createBuffer(1, sampleCount, SAMPLE_RATE);
            const channel = buffer.getChannelData(0);
            for (let i = 0; i < sampleCount; i++) {
                const s = view.getInt16(i * 2, true);
                channel[i] = s / (s < 0 ? 0x8000 : 0x7FFF);
            }

            const src = this.playCtx.createBufferSource();
            src.buffer = buffer;
            src.connect(this._outAnalyser || this.playCtx.destination);

            const now = this.playCtx.currentTime;
            const startAt = Math.max(now, this._playCursor);
            src.start(startAt);
            this._playCursor = startAt + buffer.duration;
        }

        _stopPlayback() {
            if (!this.playCtx) return;
            // Mevcut tüm planlanmış ses node'larını durdurmak için context'i suspend+resume
            try { this.playCtx.suspend().then(() => this.playCtx.resume()); } catch { }
            this._playCursor = this.playCtx.currentTime;
        }

        _scheduleListeningTransition() {
            clearTimeout(this._micReopenTimer);
            if (!this.playCtx) {
                this._setState('listening');
                return;
            }
            // Kalan ses süresi (ms) + 350ms emniyet payı
            const remaining = (this._playCursor - this.playCtx.currentTime) * 1000;
            const delay = Math.max(0, remaining) + 350;
            this._micReopenTimer = setTimeout(() => {
                if (!this._disposed && this.state === 'speaking') {
                    this._setState('listening');
                }
            }, delay);
        }

        // ─── Helpers ───

        _rms(analyser) {
            if (!analyser) return 0;
            if (!this._levelBuf || this._levelBuf.length !== analyser.fftSize)
                this._levelBuf = new Float32Array(analyser.fftSize);
            analyser.getFloatTimeDomainData(this._levelBuf);
            let sum = 0;
            for (let i = 0; i < this._levelBuf.length; i++) sum += this._levelBuf[i] * this._levelBuf[i];
            return Math.min(1, Math.sqrt(sum / this._levelBuf.length) * 4);
        }

        _setState(s) {
            this.state = s;
            // Mikrofon gating: bot konuşurken track'i kapatıp echo'yu kaynaktan kes.
            // listening/idle dönersek tekrar aç. error durumunda da kapatılı kalsın.
            if (this._micTrack) {
                if (s === 'speaking') {
                    try { this._micTrack.enabled = false; } catch { }
                } else if (s === 'listening' || s === 'idle') {
                    try { this._micTrack.enabled = !this._muted; } catch { }
                }
            }
            this._emit('state', { state: s });
        }

        _emit(event, data) {
            const cb = this.callbacks[event];
            if (typeof cb === 'function') cb(data);
        }
    }

    window.RealtimeClient = RealtimeClient;
})();
