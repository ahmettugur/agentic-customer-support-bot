// realtime-ui.js
// Mikrofon butonu + RealtimeClient ↔ ChatUI köprüsü.
// Mevcut window.chatApp'e iliştir; sessionId'yi paylaş, transcript ve asistan
// yanıtlarını chat balonu olarak ekle.

(function () {
    'use strict';

    const STATE_LABELS = {
        idle: 'Sesli konuşma',
        connecting: 'Bağlanıyor…',
        listening: '🎤 Dinliyor',
        thinking: '💭 Düşünüyor',
        speaking: '🔊 Konuşuyor',
        error: 'Hata'
    };

    function init() {
        const voiceBtn = document.getElementById('voiceBtn');
        const statusEl = document.getElementById('voiceStatus');
        const statusText = document.getElementById('voiceStatusText');
        const interruptBtn = document.getElementById('voiceInterruptBtn');
        if (!voiceBtn || !statusEl || !statusText) return;

        let client = null;

        const setStatus = (state, msg) => {
            statusEl.hidden = (state === 'idle' || state === 'error');
            statusText.textContent = msg || STATE_LABELS[state] || state;
            voiceBtn.classList.toggle('active', state !== 'idle' && state !== 'error');
            voiceBtn.title = (state === 'idle')
                ? 'Sesli konuşmayı başlat'
                : 'Sesli konuşmayı bitir';
        };

        const start = async () => {
            const app = window.chatApp;
            const sessionId = app?.api?.sessionId || null;

            // Streaming bot mesaj iskeletini (reasoning panel + agent chip + bubble)
            // text chat ile birebir aynı şekilde yöneten state.
            let streamCtx = null;
            let reasoningState = { buffer: '' };

            const finalizeBubbleIfAny = () => {
                if (streamCtx && app?.ui) {
                    try { app.ui.finalizeStreamingMessage(streamCtx); } catch { }
                }
                streamCtx = null;
                reasoningState = { buffer: '' };
            };

            client = new RealtimeClient({
                baseUrl: app?.api?.baseUrl || '',
                sessionId,
                callbacks: {
                    state: ({ state }) => setStatus(state),
                    connected: ({ sessionId: sid }) => {
                        // Backend yeni session açtıysa app'e bildir
                        if (sid && app?.api && !app.api.sessionId) {
                            app.api.setSession(sid);
                            try { app.refreshSessionList?.(); } catch { }
                        }
                    },

                    // Kullanıcı konuşmasının transcript'i: user balonunu ekle ve
                    // ardından boş bot iskeletini aç — text chat'in send akışıyla aynı.
                    user_transcript: ({ text }) => {
                        if (!text || !app?.ui) return;
                        finalizeBubbleIfAny(); // önceki tur bittiyse temizle
                        app.ui.addMessage('user', text);
                        streamCtx = app.ui.startStreamingMessage();
                        reasoningState = { buffer: '' };
                    },

                    // Backend'den ham agent pipeline event'i — text chat handler'ını yeniden kullan
                    chat_event: (evt) => {
                        if (!streamCtx || !app?._handleStreamEvent) return;
                        try {
                            app._handleStreamEvent(streamCtx, reasoningState, evt);
                        } catch (err) {
                            console.warn('Realtime chat_event handler hatası:', err);
                        }
                    },

                    // Tüm agent akışı bitti — bubble'ı finalize et (markdown render + cursor kaldır)
                    workflow_done: () => finalizeBubbleIfAny(),

                    // Asistan TTS akışı tamamlandı (response_done OpenAI'dan)
                    response_done: () => { /* bubble zaten workflow_done'da finalize oldu */ },

                    error: ({ message }) => {
                        console.warn('Realtime error:', message);
                        setStatus('error', '⚠ ' + (message || 'Hata'));
                        setTimeout(() => setStatus('idle'), 3000);
                        finalizeBubbleIfAny();
                    },
                    close: () => {
                        finalizeBubbleIfAny();
                        setStatus('idle');
                    }
                }
            });

            try {
                await client.start();
            } catch (err) {
                client = null;
                const msg = (err?.name === 'NotAllowedError')
                    ? 'Mikrofon izni reddedildi.'
                    : (err?.message || 'Sesli konuşma başlatılamadı.');
                setStatus('error', '⚠ ' + msg);
                setTimeout(() => setStatus('idle'), 3000);
            }
        };

        const stop = () => {
            if (client) {
                client.stop();
                client = null;
            }
            setStatus('idle');
        };

        voiceBtn.addEventListener('click', () => {
            if (!client) start();
            else stop();
        });

        if (interruptBtn) {
            interruptBtn.addEventListener('click', () => {
                if (client) client.interrupt();
            });
        }

        // Sayfa kapanırken bağlantıyı kapat
        window.addEventListener('beforeunload', stop);

        setStatus('idle');
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
