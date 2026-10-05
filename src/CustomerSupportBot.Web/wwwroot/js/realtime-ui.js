// realtime-ui.js
// Mikrofon butonu + RealtimeClient ↔ ChatUI köprüsü.
// Mevcut window.chatApp'e iliştir; sessionId'yi paylaş, transcript ve asistan
// yanıtlarını chat balonu olarak ekle.

(function () {
    'use strict';

    function init() {
        // Betik sayfa ömrü boyunca bir kez çalışır (loadScript aynı betiği yeniden yüklemez), ama
        // sohbet sayfası uygulama içinde her açılışta #voiceBtn'i YENİDEN çizer. Bu yüzden düğmeye
        // bir kez bağlanılmaz: tıklama belge düzeyinde dinlenir, düğme her kullanımda yeniden
        // bulunur. Eskiden ilk düğmeye bağlanılıyordu; sohbetten çıkıp dönünce 🎙 çalışmıyordu.
        if (window.__voiceUiReady) return;   // betik yeniden çalışırsa dinleyiciler çiftlenmesin
        window.__voiceUiReady = true;
        const voiceButton = () => document.getElementById('voiceBtn');

        let client = null;
        let overlayOpen = false;
        let connectedOnce = false;
        let closeTimer = null;
        let starting = false;   // c.start() sürerken — atılan hatayı launchClient gösterir

        const call = (type, a, b) => { try { window.chatApp?.voiceCall?.(type, a, b); } catch { } };

        const setStatus = (state) => {
            const isActive = state !== 'idle' && state !== 'error';
            voiceButton()?.classList.toggle('active', isActive && client !== null);
            call('state', state);
        };

        const openOverlay = () => {
            clearTimeout(closeTimer);
            closeTimer = null;
            overlayOpen = true;
            connectedOnce = false;
            call('open');
            window.voiceOrb?.start(() => client?.getLevels?.() || { input: 0, output: 0 });
            // Erişilebilirlik: odak diyaloğa taşınır (Bitir düğmesi), kapanınca 🎙'ye döner.
            setTimeout(() => document.querySelector('.vc-btn--end')?.focus(), 60);
        };

        const closeOverlay = () => {
            clearTimeout(closeTimer);
            closeTimer = null;
            if (!overlayOpen) return;
            overlayOpen = false;
            window.voiceOrb?.stop();
            call('close');
            try { voiceButton()?.focus(); } catch { }
        };

        const closeSoon = (ms) => {
            clearTimeout(closeTimer);
            closeTimer = setTimeout(closeOverlay, ms);
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

            openOverlay();
            let me = null;   // bu çağrının istemcisi — eski istemcinin geç olayları yenisini silmesin

            const callbacks = {
                state: ({ state }) => setStatus(state),
                connected: ({ sessionId: sid, tools }) => {
                    connectedOnce = true;
                    call('connected');
                    playTone('connect');
                    if (sid && app?.api && !app.api.sessionId) {
                        app.api.setSession(sid);
                        try { app.refreshSessionList?.(); } catch { }
                    }
                    console.info('[RealtimeNative] connected, tools:', tools);
                },

                muted: ({ muted }) => call('muted', muted ? 'true' : 'false'),
                speech_started: () => call('speech_started'),

                // ── Kullanıcı konuşmayı bitirdi ──
                // Transcript daha gelmeden placeholder user bubble açıyoruz.
                // Bot bubble'ı daha sonra (assistant_text_delta ile) oluşacağı
                // için DOM sırası: user placeholder → bot bubble. Doğru.
                speech_stopped: ({ itemId }) => {
                    // Kimlik ekran modeline de gider: canlı parçası olmayan transkripsiyon modellerinde
                    // turun kimliğini yalnızca bu olay taşır (altyazı bir tur geriden kalmasın).
                    call('speech_stopped', itemId || null);
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
                    call('user_delta', itemId, text);
                    const entry = ensureUserBubble(itemId);
                    entry.text += text;
                    if (!entry.timer) entry.timer = setTimeout(() => flushLive(entry), LIVE_FLUSH_MS);
                },

                // ── Kullanıcı transcript'i geldi ──
                user_transcript: ({ itemId, text }) => {
                    if (!text || !app?.ui) return;
                    call('user_final', itemId || null, text);

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
                    call('tool_call', name, TOOL_LABELS[name] || name);
                    ensureAssistantBubble();
                    showToolChip(name);
                },
                tool_result: ({ name }) => { call('tool_result', name); completeToolChip(name); },

                // ── Asistan metin delta ──
                assistant_text_delta: ({ text }) => {
                    if (!text) return;
                    call('assistant_delta', text);
                    ensureAssistantBubble();
                    assistantText += text;
                    if (app?.ui?.appendResponseChunk) {
                        try { app.ui.appendResponseChunk(assistantBubble, text); } catch { }
                    }
                },

                // Tam metin fallback
                assistant_text: ({ text }) => {
                    if (text && !assistantText) call('assistant_delta', text);
                    if (text && !assistantText && assistantBubble && app?.ui?.appendResponseChunk) {
                        try { app.ui.appendResponseChunk(assistantBubble, text); } catch { }
                    }
                },

                // ── Response bitti ──
                // DİKKAT: response_done, user transcript'ten HER ZAMAN ÖNCE gelir.
                // Bu yüzden burada pendingUserBubble'a DOKUNMUYORUZ.
                response_done: () => {
                    call('response_done');
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
                    call('ended', label);
                    playTone('end');
                    setStatus('idle');
                    closeSoon(1500);
                    // client.stop() zaten realtime-client.js tarafında çağrılıyor
                    client = null;
                },

                error: ({ message }) => {
                    console.warn('RealtimeNative error:', message);
                    // start() içinde atılan hata (mikrofon izni vb.) — launchClient gösterir.
                    if (starting) return;
                    // Bağlantı açıksa sunucu görüşmeyi sürdürüyordur (ör. girdi reddedildi):
                    // görüşme bitmez, uyarı çip olarak görünür.
                    const open = me.ws && me.ws.readyState === WebSocket.OPEN;
                    if (connectedOnce && open) {
                        call('notice', message || 'Mesaj işlenemedi.');
                        return;
                    }
                    // Ölümcül: istemci MUTLAKA durdurulur — yoksa mikrofon kayıtta kalırdı.
                    removePendingUserBubble();
                    settleAllUserBubbles();
                    finalizeAssistantBubble();
                    try { me.stop(); } catch { }
                    client = null;
                    setStatus('idle');
                    call('error', '⚠ ' + (message || 'Bağlantı koptu.'));
                    // Bağlantı hiç kurulamadıysa "Tekrar dene / Kapat" ekranı açık kalır.
                    if (connectedOnce) closeSoon(2000);
                },
                close: () => {
                    removePendingUserBubble();
                    settleAllUserBubbles();
                    finalizeAssistantBubble();
                    if (client !== me) return;   // kullanıcı bitirdi ya da yeni görüşme başladı
                    setStatus('idle');
                    // Bağlantı beklenmedik kapandıysa (bitir/hata/sonlandırma dışında) ekranı kapat.
                    if (overlayOpen && !closeTimer) { playTone('end'); closeOverlay(); }
                    client = null;
                }
            };

            // Bu istemcinin olayları yalnızca o hâlâ geçerli istemciyse işlenir: bitirilmiş ya da
            // yerine yenisi açılmış bir istemcinin geç gelen olayları (ör. kapanan soketin
            // onerror'ı) yeni görüşmenin ekranını bozmamalı.
            for (const name of Object.keys(callbacks)) {
                const handler = callbacks[name];
                callbacks[name] = (data) => { if (client === me) handler(data); };
            }

            client = me = new RealtimeClient({
                baseUrl: app?.api?.baseUrl || '',
                sessionId,
                endpoint: '/chat/realtime-native',
                callbacks
            });

            await launchClient(client);
        };

        const launchClient = async (c) => {
            starting = true;
            try {
                await c.start();
            } catch (err) {
                client = null;
                const msg = (err?.name === 'NotAllowedError')
                    ? 'Mikrofon izni reddedildi. Tarayıcı ayarlarından izin verip tekrar deneyin.'
                    : (err?.message || 'Sesli konuşma başlatılamadı.');
                call('error', '⚠ ' + msg);
                setStatus('idle');
            } finally {
                starting = false;
            }
        };

        const stop = () => {
            const wasActive = client !== null;
            if (client) {
                client.stop();
                client = null;
            }
            setStatus('idle');
            if (wasActive) playTone('end');
            closeOverlay();
        };

        // Blazor tarafından çağrılabilir (ör. human_joined → sesli kanalı kapat)
        window.__stopVoice = stop;

        // VoiceCallOverlay düğmeleri (Chat.razor → JS). İstemci tek doğruluk kaynağı: sessiz durumu
        // istemciden 'muted' olayıyla geri döner.
        window.__voiceCall = {
            toggleMute: () => { if (client) client.setMuted(!client.muted); },
            end: () => stop(),
            interrupt: () => { if (client) client.interrupt(); },
            retry: () => { if (client) return; primeTones(); startVoice(); }
        };

        // Klavye: ekran açıkken Space sessize al, Esc bitir. Space'in varsayılanı (odaktaki düğmeyi
        // tıklama) hem keydown hem keyup'ta engellenir — aksi hâlde odak bir düğmedeyken iki kez
        // tetiklenirdi.
        const isTyping = (t) => t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
        // Kısayollar yalnızca ekran GERÇEKTEN görünürken: sayfada görüşme ekranı yoksa (chatApp.voiceCall
        // kayıtlı değil) overlayOpen yine true olur ve Space/Esc görünmeyen bir ekran için yutulurdu.
        const overlayVisible = () => overlayOpen && !!document.querySelector('.vc-overlay');
        document.addEventListener('keydown', (e) => {
            if (!overlayVisible()) return;
            // Odak tuzağı: ekran açıkken Tab yalnızca ekrandaki düğmeler arasında döner; arkadaki
            // (görünmeyen) sohbet alanlarına odak kaçmaz.
            if (e.key === 'Tab') {
                const items = [...document.querySelectorAll('.vc-overlay button:not(:disabled)')];
                if (items.length === 0) return;
                const i = items.indexOf(document.activeElement);
                const next = e.shiftKey ? (i <= 0 ? items.length - 1 : i - 1) : (i === -1 || i === items.length - 1 ? 0 : i + 1);
                e.preventDefault();
                items[next].focus();
                return;
            }
            if (isTyping(e.target)) return;
            if (e.key === 'Escape') { e.preventDefault(); stop(); }
            else if (e.key === ' ' || e.code === 'Space') {
                e.preventDefault();
                if (!e.repeat) window.__voiceCall.toggleMute();
            }
        });
        document.addEventListener('keyup', (e) => {
            if (overlayVisible() && (e.key === ' ' || e.code === 'Space') && !isTyping(e.target)) e.preventDefault();
        });

        // Sesli butonu — açıksa kapatır, kapalıysa başlatır (belge düzeyinde; yukarıdaki nota bakın)
        document.addEventListener('click', (e) => {
            if (!e.target?.closest?.('#voiceBtn')) return;
            if (client) stop();
            else { primeTones(); startVoice(); }
        });

        window.addEventListener('beforeunload', stop);
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

    // Kısa görüşme sesleri — dosya yok, Web Audio ile üretilir. Kısık seviye; tarayıcı engellerse sessizce geçer.
    //
    // Otomatik oynatma politikası: kullanıcı hareketi DIŞINDA oluşturulan AudioContext "suspended" başlar.
    // Bağlam eskiden ilk sesle (bağlantı kurulunca, yani tıklamadan saniyeler sonra) oluşturuluyordu ve hiç
    // devam ettirilmediği için sesler çalmıyordu. Artık 🎙 tıklamasında (hareketin içinde) açılır; askıdaysa
    // her seste resume denenir.
    let toneCtx = null;
    function primeTones() {
        try {
            toneCtx = toneCtx || new (window.AudioContext || window.webkitAudioContext)();
            if (toneCtx.state === 'suspended') toneCtx.resume().catch(() => { });
        } catch { /* ses yoksa görüşme etkilenmez */ }
    }

    function playTone(kind) {
        try {
            primeTones();
            if (!toneCtx) return;
            if (toneCtx.state === 'suspended') {
                toneCtx.resume().then(() => scheduleTone(kind)).catch(() => { });
                return;
            }
            scheduleTone(kind);
        } catch { /* ses çalınamazsa görüşme etkilenmez */ }
    }

    function scheduleTone(kind) {
        try {
            const notes = kind === 'connect' ? [660, 880] : [660, 440];
            const t0 = toneCtx.currentTime;
            notes.forEach((freq, i) => {
                const osc = toneCtx.createOscillator();
                const gain = toneCtx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq;
                const start = t0 + i * 0.12;
                gain.gain.setValueAtTime(0.0001, start);
                gain.gain.exponentialRampToValueAtTime(0.06, start + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.11);
                osc.connect(gain).connect(toneCtx.destination);
                osc.start(start);
                osc.stop(start + 0.12);
            });
        } catch { /* ses çalınamazsa görüşme etkilenmez */ }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
