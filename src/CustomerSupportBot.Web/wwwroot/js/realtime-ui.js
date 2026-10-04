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
            statusText.textContent = msg || (STATE_LABELS[state] || state);
            const isActive = state !== 'idle' && state !== 'error';
            voiceBtn.classList.toggle('active', isActive && client !== null);
        };

        // ─── Sesli görüşme — gpt-realtime modeli doğrudan konuşur, yalnızca okuma tool'ları ───
        // (Eskiden ayrıca bir "köprü modu" vardı: model yalnızca STT/TTS yapıyor, yanıtı agent
        // pipeline üretiyordu. Kaldırıldı; tek sesli mod budur.)

        const startVoice = async () => {
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
            //
            // CANLI ALTYAZI: sunucu olayları konuşma kimliği (itemId) taşır. Balonlar bu kimliğe
            // göre tutulur (userBubbles); gpt-live-transcribe'da parçalar konuşma SÜRERKEN
            // gelir, balon konuşma bitmeden açılır ve metin akar. Son transkript gelince balon
            // birleşik parçalarla değil son metinle doldurulur — sağlayıcı parçaları sonradan
            // düzeltebilir. Kimlik taşımayan olaylar için pendingUserBubble (eski yol) kalır.
            let pendingUserBubble = null;
            const userBubbles = new Map();   // itemId → { handle, text, timer }
            const LIVE_FLUSH_MS = 100;       // parçaları toplayıp tek seferde Blazor'a gönder

            // ── Helpers ──

            // Balonlar tutamaçtır ({ id }) — içerik Blazor'da çizilir (bkz. chat-bridge.js ui).
            const removePendingUserBubble = () => {
                if (!pendingUserBubble) return;
                try { app?.ui?.removeMessage(pendingUserBubble); } catch { }
                pendingUserBubble = null;
            };

            // itemId'ye ait balonu döndürür; yoksa yer tutucu olarak açar.
            const ensureUserBubble = (itemId) => {
                let entry = userBubbles.get(itemId);
                if (!entry) {
                    entry = { handle: app.ui.addMessage('user', '…', { placeholder: true }), text: '', timer: null };
                    userBubbles.set(itemId, entry);
                }
                return entry;
            };

            const flushLive = (entry) => {
                entry.timer = null;
                if (!entry.text) return;
                try { app?.ui?.setMessageText(entry.handle, entry.text, true); } catch { }
            };

            // Konuşma bitti ama son transkript gelmeyecek (hata/kapanış): metni olan balon
            // canlı stilinden çıkarılır, boş yer tutucu silinir.
            const settleUserBubble = (itemId, entry) => {
                if (entry.timer) { clearTimeout(entry.timer); entry.timer = null; }
                try {
                    if (entry.text) app?.ui?.setMessageText(entry.handle, entry.text, false);
                    else app?.ui?.removeMessage(entry.handle);
                } catch { }
                userBubbles.delete(itemId);
            };

            const settleAllUserBubbles = () => {
                for (const [id, entry] of [...userBubbles]) settleUserBubble(id, entry);
            };

            const fillPendingUserBubble = (text) => {
                if (!pendingUserBubble) return false;
                try { app?.ui?.setMessageText(pendingUserBubble, text, false); } catch { }
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
                    // Boş balon bırakmak yerine kaldır.
                    try { app.ui.removeMessage(assistantBubble); } catch { }
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
                    speech_stopped: ({ itemId }) => {
                        if (!app?.ui) return;
                        if (itemId) {
                            // Hiç metin almamış önceki yer tutucular gürültü/sessizlikten kalan
                            // hayaletlerdir — silinir. Metni olanlar KALIR: son transkriptleri
                            // kendi kimlikleriyle sonradan gelebilir.
                            for (const [id, entry] of [...userBubbles]) {
                                if (id !== itemId && !entry.text) settleUserBubble(id, entry);
                            }
                            ensureUserBubble(itemId);   // canlı parçalarla zaten açıldıysa aynısı
                            return;
                        }
                        // Önceki tur transcript gelmeden yeni tur başladıysa eski
                        // placeholder'ı temizle (gürültü/sessizlikten kalan hayalet).
                        removePendingUserBubble();
                        pendingUserBubble = app.ui.addMessage('user', '…', { placeholder: true });
                    },

                    // ── Canlı altyazı parçası ──
                    user_transcript_delta: ({ itemId, text }) => {
                        if (!itemId || !text || !app?.ui) return;
                        const entry = ensureUserBubble(itemId);
                        entry.text += text;
                        if (!entry.timer) entry.timer = setTimeout(() => flushLive(entry), LIVE_FLUSH_MS);
                    },

                    // ── Kullanıcı transcript'i geldi ──
                    user_transcript: ({ itemId, text }) => {
                        if (!text || !app?.ui) return;

                        // 0) Kimlikli balon (canlı altyazı ya da speech_stopped yer tutucusu):
                        //    SON METİNLE doldur — parçaların birleşimiyle değil.
                        const live = itemId ? userBubbles.get(itemId) : null;
                        if (live) {
                            if (live.timer) { clearTimeout(live.timer); live.timer = null; }
                            try { app.ui.setMessageText(live.handle, text, false); } catch { }
                            userBubbles.delete(itemId);
                            app.ui.scrollToBottom();
                            return;
                        }

                        // 1) Placeholder var → metnini doldur. Sıralama zaten doğru.
                        if (fillPendingUserBubble(text)) {
                            if (app?.ui) app.ui.scrollToBottom();
                            return;
                        }

                        // 2) Placeholder yok (beklenmedik durum). Yeni user bubble oluştur.
                        //    Bot bubble varsa onun ÖNÜNE yerleştir; yoksa normale ekle.
                        app.ui.addMessage('user', text, { before: assistantBubble });
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
                        settleAllUserBubbles();
                        finalizeAssistantBubble();
                        const label = reason === 'idle_timeout'
                            ? 'Görüşme sessizlik nedeniyle sonlandırıldı.'
                            : 'Görüşme sonlandırıldı.';
                        setStatus('idle', label);
                        setTimeout(() => setStatus('idle'), 3000);
                        // client.stop() zaten realtime-client.js tarafında çağrılıyor
                        client = null;
                    },

                    error: ({ message }) => {
                        console.warn('RealtimeNative error:', message);
                        setStatus('error', '⚠ ' + (message || 'Hata'));
                        setTimeout(() => setStatus('idle'), 3000);
                        removePendingUserBubble();
                        settleAllUserBubbles();
                        finalizeAssistantBubble();
                    },
                    close: () => {
                        removePendingUserBubble();
                        settleAllUserBubbles();
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

        // Blazor tarafından çağrılabilir (ör. human_joined → sesli kanalı kapat)
        window.__stopVoice = stop;

        // Sesli butonu — açıksa kapatır, kapalıysa başlatır
        voiceBtn.addEventListener('click', () => {
            if (client) stop();
            else startVoice();
        });

        if (interruptBtn) {
            interruptBtn.addEventListener('click', () => {
                if (client) client.interrupt();
            });
        }

        window.addEventListener('beforeunload', stop);
        setStatus('idle');
    }

    // Sesli modda chip etiketleri — kullanıcıya teknik isim yerine dostça gösterilir
    const TOOL_LABELS = {
        product_inquiry_tool: 'Ürün sorgulanıyor',
        product_list_tool: 'Ürünler listeleniyor',
        order_status_tool: 'Sipariş durumu sorgulanıyor',
        get_last_order_tool: 'Son sipariş getiriliyor',
        get_all_orders_tool: 'Siparişler listeleniyor',
        complaint_status_tool: 'Şikayet durumu sorgulanıyor',
        get_all_complaints_tool: 'Şikayetler listeleniyor',
        // Yan etkili işlemler hemen gerçekleşmez — onaya gönderilir
        order_placement_tool: 'Sipariş talebi onaya gönderiliyor',
        order_cancel_tool: 'İptal talebi onaya gönderiliyor',
        return_request_tool: 'İade talebi onaya gönderiliyor',
        complaint_registration_tool: 'Şikayet kaydı onaya gönderiliyor',
        human_handoff_tool: 'Temsilci talebi oluşturuluyor'
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
