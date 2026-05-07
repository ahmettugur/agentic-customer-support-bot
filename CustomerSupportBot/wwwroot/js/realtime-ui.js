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
                    user_transcript: ({ text }) => {
                        if (text && app?.ui) app.ui.addMessage('user', text);
                    },
                    assistant_text: ({ text }) => {
                        if (text && app?.ui) app.ui.addMessage('bot', text);
                    },
                    error: ({ message }) => {
                        console.warn('Realtime error:', message);
                        setStatus('error', '⚠ ' + (message || 'Hata'));
                        setTimeout(() => setStatus('idle'), 3000);
                    },
                    close: () => setStatus('idle')
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
