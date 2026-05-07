// realtime-client.js
// Tarayıcı tarafı OpenAI Realtime köprü istemcisi.
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
//   client.stop();

(function () {
    'use strict';

    const SAMPLE_RATE = 24000;

    class RealtimeClient {
        constructor({ baseUrl, sessionId, callbacks }) {
            this.baseUrl = baseUrl || ''; // boş ise aynı origin
            this.sessionId = sessionId || null;
            this.callbacks = callbacks || {};

            this.ws = null;
            this.mediaStream = null;
            this.audioCtx = null;
            this.workletNode = null;
            this.sourceNode = null;

            // Asistan TTS playback için ayrı context
            this.playCtx = null;
            this._playCursor = 0; // sıralı playback için zaman damgası

            this.state = 'idle'; // idle | connecting | listening | speaking | error
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

        interrupt() {
            // Sadece asistan konuşurken anlamlı; aksi halde OpenAI
            // "no active response" uyarısı verir. Local playback'i her durumda durdur.
            this._stopPlayback();
            if (this.state === 'speaking') {
                this._sendControl({ type: 'interrupt' });
            }
        }

        stop() {
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

            // Capture context — 24kHz
            this.audioCtx = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: SAMPLE_RATE
            });

            await this.audioCtx.audioWorklet.addModule('js/realtime-pcm-worklet.js');

            this.sourceNode = this.audioCtx.createMediaStreamSource(this.mediaStream);
            this.workletNode = new AudioWorkletNode(this.audioCtx, 'pcm-capture-processor');
            this.workletNode.port.onmessage = (e) => this._onAudioChunk(e.data);
            this.sourceNode.connect(this.workletNode);
            // Worklet'i destination'a bağlamıyoruz — kullanıcı kendi sesini duymasın

            // Playback context (asistanı çalmak için) — aynı sample rate
            this.playCtx = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: SAMPLE_RATE
            });
            this._playCursor = this.playCtx.currentTime;
        }

        _onAudioChunk(arrayBuffer) {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
            this.ws.send(arrayBuffer);
        }

        // ─── WS ───

        async _connectWs() {
            const proto = (location.protocol === 'https:') ? 'wss:' : 'ws:';
            const host = this.baseUrl
                ? this.baseUrl.replace(/^https?:/i, proto)
                : proto + '//' + location.host;
            const url = host + '/chat/realtime/' + (this.sessionId || '');

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
            if (typeof e.data === 'string') {
                let msg;
                try { msg = JSON.parse(e.data); } catch { return; }
                this._handleControl(msg);
            } else if (e.data instanceof ArrayBuffer) {
                this._enqueueAudio(e.data);
            } else if (e.data instanceof Blob) {
                e.data.arrayBuffer().then(buf => this._enqueueAudio(buf));
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
                case 'workflow_start':
                    this._setState('thinking');
                    this._emit('workflow_start', msg);
                    break;
                case 'workflow_done':
                    this._emit('workflow_done', msg);
                    break;
                case 'assistant_text':
                    this._setState('speaking');
                    this._emit('assistant_text', msg);
                    break;
                case 'assistant_text_delta':
                    this._emit('assistant_text_delta', msg);
                    break;
                case 'response_done':
                    this._setState('listening');
                    this._emit('response_done', msg);
                    break;
                case 'error':
                    // İki kaynak: (a) RealtimeBridge bağlantı/sistem hatası ({type:"error", message:"..."}),
                    // (b) agent pipeline'dan forward edilen StreamEvent ({type:"error", data:{message:"..."}}).
                    // İkincisi sadece o turun bubble'ına uyarı eklenmeli; bağlantı durumu değişmemeli.
                    if (msg.data) {
                        this._emit('chat_event', { type: 'error', data: msg.data });
                    } else {
                        this._setState('error');
                        this._emit('error', msg);
                    }
                    break;
                default:
                    // Agent pipeline event'leri (reasoning_*, agent, response_*, done...)
                    // text chat SSE sözleşmesiyle aynı; UI tarafına ham olarak yansıtırız.
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
            src.connect(this.playCtx.destination);

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

        // ─── Helpers ───

        _setState(s) {
            this.state = s;
            this._emit('state', { state: s });
        }

        _emit(event, data) {
            const cb = this.callbacks[event];
            if (typeof cb === 'function') cb(data);
        }
    }

    window.RealtimeClient = RealtimeClient;
})();
