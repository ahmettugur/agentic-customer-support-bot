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
        const voiceNativeBtn = document.getElementById('voiceNativeBtn');
        const statusEl = document.getElementById('voiceStatus');
        const statusText = document.getElementById('voiceStatusText');
        const interruptBtn = document.getElementById('voiceInterruptBtn');
        if (!voiceBtn || !statusEl || !statusText) return;

        let client = null;
        let activeMode = null; // 'bridge' | 'native' | null

        const setStatus = (state, msg) => {
            statusEl.hidden = (state === 'idle' || state === 'error');
            const prefix = activeMode === 'native' ? '⚡ ' : '';
            statusText.textContent = msg || (prefix + (STATE_LABELS[state] || state));
            const isActive = state !== 'idle' && state !== 'error';
            voiceBtn.classList.toggle('active', isActive && activeMode === 'bridge');
            if (voiceNativeBtn) {
                voiceNativeBtn.classList.toggle('active', isActive && activeMode === 'native');
            }
        };

        // ─── Köprü modu (mevcut) — agent pipeline UI'sini kullanır ───

        const startBridge = async () => {
            const app = window.chatApp;
            const sessionId = app?.api?.sessionId || null;

            let streamCtx = null;
            let reasoningState = { buffer: '' };

            const finalizeBubbleIfAny = () => {
                if (streamCtx && app?.ui) {
                    try { app.ui.finalizeStreamingMessage(streamCtx); } catch { }
                }
                streamCtx = null;
                reasoningState = { buffer: '' };
            };

            activeMode = 'bridge';
            client = new RealtimeClient({
                baseUrl: app?.api?.baseUrl || '',
                sessionId,
                endpoint: '/chat/realtime',
                callbacks: {
                    state: ({ state }) => setStatus(state),
                    connected: ({ sessionId: sid }) => {
                        if (sid && app?.api && !app.api.sessionId) {
                            app.api.setSession(sid);
                            try { app.refreshSessionList?.(); } catch { }
                        }
                    },
                    user_transcript: ({ text }) => {
                        if (!text || !app?.ui) return;
                        finalizeBubbleIfAny();
                        app.ui.addMessage('user', text);
                        streamCtx = app.ui.startStreamingMessage();
                        reasoningState = { buffer: '' };
                    },
                    chat_event: (evt) => {
                        if (!streamCtx || !app?._handleStreamEvent) return;
                        try { app._handleStreamEvent(streamCtx, reasoningState, evt); }
                        catch (err) { console.warn('chat_event handler hatası:', err); }
                    },
                    workflow_done: () => finalizeBubbleIfAny(),
                    response_done: () => { },
                    error: ({ message }) => {
                        console.warn('Realtime error:', message);
                        setStatus('error', '⚠ ' + (message || 'Hata'));
                        setTimeout(() => setStatus('idle'), 3000);
                        finalizeBubbleIfAny();
                    },
                    close: () => { finalizeBubbleIfAny(); setStatus('idle'); }
                }
            });

            await launchClient(client);
        };

        // ─── Native modu (yeni) — gpt-realtime-1.5 doğrudan, tool subset kısıtlı ───

        const startNative = async () => {
            const app = window.chatApp;
            const sessionId = app?.api?.sessionId || null;

            // ── State ──
            let assistantBubble = null;   // Şu an streaming edilen bot baloncuğu (streamCtx)
            let assistantText = '';        // Bot baloncuğunun biriken text'i
            let toolCallShownForCurrentTurn = false;

            // Placeholder kullanıcı baloncuğu.
            //
            // KRİTİK ZAMANLAMA: OpenAI Realtime API'de olay sırası şöyledir:
            //   speech_stopped → response.created → audio.delta/text_delta... →
            //   response.done → ... → input_audio_transcription.completed
            //
            // Yani response_done, user transcript'ten HER ZAMAN ÖNCE gelir
            // (transcription asenkron). Bu nedenle:
            //  - Placeholder'ı speech_stopped'ta açıyoruz (bot bubble'ından önce DOM'da)
            //  - response_done'da SİLMİYORUZ (transcript henüz gelmemiş olabilir!)
            //  - Yalnızca user_transcript ile doldurulur
            //  - Yalnızca yeni bir speech_stopped (yeni tur) veya error/close silebilir
            let pendingUserBubble = null;

            // ── Helpers ──

            const removePendingUserBubble = () => {
                if (!pendingUserBubble) return;
                try { pendingUserBubble.remove(); } catch { }
                pendingUserBubble = null;
            };

            const fillPendingUserBubble = (text) => {
                if (!pendingUserBubble) return false;
                const bubbleEl = pendingUserBubble.querySelector('.message-bubble');
                if (bubbleEl) bubbleEl.textContent = text;
                pendingUserBubble.classList.remove('placeholder');
                pendingUserBubble = null;
                return true;
            };

            const ensureAssistantBubble = () => {
                if (assistantBubble || !app?.ui) return;
                assistantBubble = app.ui.startStreamingMessage();
                assistantText = '';
                toolCallShownForCurrentTurn = false;
            };

            const finalizeAssistantBubble = () => {
                if (!assistantBubble) return;
                if (!assistantText && app?.ui) {
                    // Bot baloncuğunda hiç metin yok (ör. tool-call-only yanıtı).
                    // Boş balon bırakmak yerine DOM'dan kaldır.
                    try { assistantBubble.messageDiv.remove(); } catch { }
                } else if (app?.ui) {
                    try { app.ui.finalizeStreamingMessage(assistantBubble); } catch { }
                }
                assistantBubble = null;
                assistantText = '';
            };

            const showToolChip = (name) => {
                if (!assistantBubble || toolCallShownForCurrentTurn) return;
                toolCallShownForCurrentTurn = true;
                const friendly = TOOL_LABELS[name] || name;
                if (app?.ui?.setAgentStatus) {
                    try { app.ui.setAgentStatus(assistantBubble, friendly, 'running'); } catch { }
                }
            };

            const completeToolChip = (name) => {
                if (!assistantBubble) return;
                const friendly = TOOL_LABELS[name] || name;
                if (app?.ui?.setAgentStatus) {
                    try { app.ui.setAgentStatus(assistantBubble, friendly, 'completed'); } catch { }
                }
            };

            // ── Client ──

            activeMode = 'native';
            client = new RealtimeClient({
                baseUrl: app?.api?.baseUrl || '',
                sessionId,
                endpoint: '/chat/realtime-native',
                callbacks: {
                    state: ({ state }) => setStatus(state),
                    connected: ({ sessionId: sid, tools }) => {
                        if (sid && app?.api && !app.api.sessionId) {
                            app.api.setSession(sid);
                            try { app.refreshSessionList?.(); } catch { }
                        }
                        console.info('[RealtimeNative] connected, tools:', tools);
                    },

                    // ── Kullanıcı konuşmayı bitirdi ──
                    // Transcript daha gelmeden placeholder user bubble açıyoruz.
                    // Bot bubble'ı daha sonra (assistant_text_delta ile) oluşacağı
                    // için DOM sırası: user placeholder → bot bubble. Doğru.
                    speech_stopped: () => {
                        if (!app?.ui) return;
                        // Önceki tur transcript gelmeden yeni tur başladıysa eski
                        // placeholder'ı temizle (gürültü/sessizlikten kalan hayalet).
                        removePendingUserBubble();
                        const ph = app.ui.addMessage('user', '…');
                        if (ph) {
                            ph.classList.add('placeholder');
                            pendingUserBubble = ph;
                        }
                    },

                    // ── Kullanıcı transcript'i geldi ──
                    user_transcript: ({ text }) => {
                        if (!text || !app?.ui) return;

                        // 1) Placeholder var → metnini doldur. Sıralama zaten doğru.
                        if (fillPendingUserBubble(text)) {
                            if (app?.ui) app.ui.scrollToBottom();
                            return;
                        }

                        // 2) Placeholder yok (beklenmedik durum). Yeni user bubble oluştur.
                        //    Bot bubble varsa onun ÖNÜNE yerleştir; yoksa normale ekle.
                        const userMsg = app.ui.addMessage('user', text);
                        const botEl = assistantBubble?.messageDiv;
                        if (userMsg && botEl && botEl.parentNode) {
                            try { botEl.parentNode.insertBefore(userMsg, botEl); } catch { }
                        }
                    },

                    // ── Tool call ──
                    tool_call: ({ name, arguments: args }) => {
                        ensureAssistantBubble();
                        showToolChip(name);
                    },
                    tool_result: ({ name }) => completeToolChip(name),

                    // ── Asistan metin delta ──
                    assistant_text_delta: ({ text }) => {
                        if (!text) return;
                        ensureAssistantBubble();
                        assistantText += text;
                        if (app?.ui?.appendResponseChunk) {
                            try { app.ui.appendResponseChunk(assistantBubble, text); } catch { }
                        }
                    },

                    // Tam metin fallback
                    assistant_text: ({ text }) => {
                        if (text && !assistantText && assistantBubble && app?.ui?.appendResponseChunk) {
                            try { app.ui.appendResponseChunk(assistantBubble, text); } catch { }
                        }
                    },

                    // ── Response bitti ──
                    // DİKKAT: response_done, user transcript'ten HER ZAMAN ÖNCE gelir.
                    // Bu yüzden burada pendingUserBubble'a DOKUNMUYORUZ.
                    response_done: () => {
                        finalizeAssistantBubble();
                    },

                    // ── Görüşme nazikçe sonlandırıldı (end_conversation tool veya idle timeout) ──
                    conversation_ended: ({ reason }) => {
                        removePendingUserBubble();
                        finalizeAssistantBubble();
                        const label = reason === 'idle_timeout'
                            ? 'Görüşme sessizlik nedeniyle sonlandırıldı.'
                            : 'Görüşme sonlandırıldı.';
                        setStatus('idle', label);
                        setTimeout(() => setStatus('idle'), 3000);
                        // client.stop() zaten realtime-client.js tarafında çağrılıyor
                        client = null;
                        activeMode = null;
                    },

                    error: ({ message }) => {
                        console.warn('RealtimeNative error:', message);
                        setStatus('error', '⚠ ' + (message || 'Hata'));
                        setTimeout(() => setStatus('idle'), 3000);
                        removePendingUserBubble();
                        finalizeAssistantBubble();
                    },
                    close: () => {
                        removePendingUserBubble();
                        finalizeAssistantBubble();
                        setStatus('idle');
                    }
                }
            });

            await launchClient(client);
        };

        const launchClient = async (c) => {
            try {
                await c.start();
            } catch (err) {
                client = null;
                activeMode = null;
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
            activeMode = null;
            setStatus('idle');
        };

        // Blazor tarafından çağrılabilir (ör. human_joined → sesli kanalı kapat)
        window.__stopVoice = stop;

        // Sesli butonu — modlar mutually exclusive: aktif farklı modu durdur, istenen modu başlat
        voiceBtn.addEventListener('click', () => {
            if (activeMode === 'bridge') stop();
            else { stop(); startBridge(); }
        });

        if (voiceNativeBtn) {
            voiceNativeBtn.addEventListener('click', () => {
                if (activeMode === 'native') stop();
                else { stop(); startNative(); }
            });
        }

        if (interruptBtn) {
            interruptBtn.addEventListener('click', () => {
                if (client) client.interrupt();
            });
        }

        window.addEventListener('beforeunload', stop);
        setStatus('idle');
    }

    // Native modda chip etiketleri — kullanıcıya teknik isim yerine dostça gösterilir
    const TOOL_LABELS = {
        product_inquiry_tool: 'Ürün sorgulanıyor',
        order_status_tool: 'Sipariş durumu sorgulanıyor',
        get_last_order_tool: 'Son sipariş getiriliyor',
        get_all_orders_tool: 'Siparişler listeleniyor'
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
