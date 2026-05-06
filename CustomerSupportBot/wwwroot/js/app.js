// Js/app.js
// Uygulama orchestrator — ChatApiClient + ChatUI'yi birleştirir

class ChatApp {
    ui;
    api;
    // HITL Live Takeover — session başına persistent SSE (admin→user canlı kanal)
    persistentEvents = null;
    persistentSessionId = null;
    // Pending handoff: 2 dk sonra "hâlâ müsait temsilci yok" bilgi pili
    pendingHandoffTimer = null;
    // Mesaj sayacı — rating widget'ı göstermek için
    messageCount = 0;
    ratingShown = false;    // Admin ile canlı sohbet aktif mi — aktifken puanlama bastırılır
    humanModeActive = false;
    constructor(baseUrl) {
        this.ui = new ChatUI();
        this.api = new ChatApiClient(baseUrl);
    }

    init() {
        this.ui.onSend(() => this.handleSend());
        this.ui.onExampleChipClick((text) => this.send(text));
        this.ui.onNewChat(() => this.newChat());
        this.ui.focusInput();
        this.refreshSidebar();

        // Sayfa kapanırken persistent bağlantıyı temiz kapat
        window.addEventListener("beforeunload", () => this._closePersistentEvents());
    }

    newChat() {
        this._closePersistentEvents();
        this.api.resetSession();
        this.ui.clearMessages();
        // Welcome ekranını yeniden yükle
        location.reload();
    }

    async switchSession(sessionId) {
        this.api.setSession(sessionId);

        // Mesaj alanını temizle + banner'ları sıfırla
        this.ui.clearMessages();
        this.ui.setHumanMode(false);
        this.ui.setPendingHandoff(false);
        this._clearPendingHandoffTimer();
        this.messageCount = 0;
        this.ratingShown = false;
        this.humanModeActive = false;

        // Yeni session için persistent bağlantıyı aç (ya da yeniden aç)
        this._ensurePersistentEvents(sessionId);

        // Seçilen oturumun mesajlarını yükle
        const messages = await this.api.getSessionMessages(sessionId);
        messages.forEach(msg => {
            this.ui.addMessage(msg.role, msg.text);
        });
        this.messageCount = messages.length;

        // Mevcut rating varsa göster
        try {
            const rating = await this.api.getRating(sessionId);
            if (rating) {
                this.ui.showExistingRating(rating);
                this.ratingShown = true;
            }
        } catch {}

        // Sidebar'ı güncelle
        this.refreshSidebar();
    }

    /**
     * Session ID bilindiğinde persistent EventSource'u açar. /chat/stream
     * per-request, bu kanal ise per-session ve sayfa açık olduğu sürece
     * dinler. Admin canlı yazdığında mesajlar buradan gelir.
     */
    _ensurePersistentEvents(sessionId) {
        if (!sessionId) return;
        if (this.persistentSessionId === sessionId && this.persistentEvents) return;

        this._closePersistentEvents();

        const url = `${this.api.baseUrl || ""}/chat/events/${sessionId}`;
        const es = new EventSource(url);
        this.persistentEvents = es;
        this.persistentSessionId = sessionId;

        es.addEventListener("human_joined", (ev) => {
            try {
                const data = JSON.parse(ev.data);
                // Human mode başladı → pending timeout'u iptal et, banner'lar
                // SetHumanMode içinde otomatik değişir.
                this._clearPendingHandoffTimer();
                this.ui.setHumanMode(true, data.humanAgent);
                this.humanModeActive = true;
            } catch {}
        });

        es.addEventListener("human_left", () => {
            this.ui.setHumanMode(false);
            this.humanModeActive = false;
            this.ui.addSystemNotice(
                "ℹ️ Sohbet bot moduna döndü — yeni mesajınız tekrar bot tarafından yanıtlanacak.");
            // Admin sohbeti bitti — şimdi puanlama gösterilebilir (yeterli mesaj varsa)
            this._maybeShowRating();
        });

        es.addEventListener("bot_typing", (ev) => {
            try {
                const data = JSON.parse(ev.data);
                if (data.on) {
                    this.ui.showTypingIndicator();
                    this.ui.setInputEnabled(false);
                } else {
                    this.ui.hideTypingIndicator();
                    if (!this.humanModeActive) this.ui.setInputEnabled(true);
                }
            } catch {}
        });

        es.addEventListener("human_message", (ev) => {
            try {
                const data = JSON.parse(ev.data);
                if (data.from === "system") {
                    this.ui.addSystemNotice(data.text || "");
                } else if (data.from === "admin") {
                    this.ui.addHumanMessage(data.text || "", data.humanAgent);
                } else if (data.from === "bot") {
                    // Replan sonrası backend tarafından otomatik tetiklenmiş bot
                    // yanıtı — normal bir asistan balonu olarak göster.
                    this.ui.addMessage("assistant", data.text || "");
                }
            } catch {}
        });

        // HITL pending handoff — eskalasyon oluştu, admin henüz devralmadı
        es.addEventListener("handoff_pending", (ev) => {
            try {
                const data = JSON.parse(ev.data);
                this.ui.setPendingHandoff(true, data.reason);
                this._startPendingHandoffTimer();
            } catch {}
        });

        // Admin devralmadan pending handoff kapatıldı (dismiss / manual resolve)
        es.addEventListener("handoff_cleared", () => {
            this._clearPendingHandoffTimer();
            this.ui.setPendingHandoff(false);
            this.ui.addSystemNotice(
                "ℹ️ Talebiniz kapatıldı. Şu anda size bağlanabilecek bir temsilci " +
                "bulunamadı; dilerseniz yeniden yazabilirsiniz.");
        });

        es.addEventListener("error", () => {
            // EventSource otomatik reconnect eder; log da atmaya gerek yok
        });
    }

    _closePersistentEvents() {
        if (this.persistentEvents) {
            try { this.persistentEvents.close(); } catch {}
            this.persistentEvents = null;
            this.persistentSessionId = null;
        }
        this._clearPendingHandoffTimer();
    }

    /**
     * 2 dakika sonra "hâlâ müsait temsilci yok" bilgi pili gönderir.
     * Admin hızla bağlanırsa (human_joined) veya pending kapanırsa
     * (handoff_cleared) timer iptal olur.
     */
    _startPendingHandoffTimer() {
        this._clearPendingHandoffTimer();
        this.pendingHandoffTimer = setTimeout(() => {
            this.ui.addSystemNotice(
                "⏳ Temsilcimiz hâlâ müsait değil. Beklemeye devam edebilir " +
                "veya daha sonra yeniden yazabilirsiniz.");
        }, 2 * 60 * 1000);
    }

    _clearPendingHandoffTimer() {
        if (this.pendingHandoffTimer) {
            clearTimeout(this.pendingHandoffTimer);
            this.pendingHandoffTimer = null;
        }
    }

    async refreshSidebar() {
        const sessions = await this.api.getSessions();
        this.ui.renderSessionList(
            sessions,
            this.api.sessionId,
            (sessionId) => this.switchSession(sessionId)
        );
    }

    handleSend() {
        const text = this.ui.inputText;
        if (!text) return;
        this.ui.clearInput();
        this.send(text);
    }

    async send(text) {
        this.ui.addMessage("user", text);
        this.ui.setInputEnabled(false);
        this.messageCount++;

        // Streaming mesaj iskeletini oluştur
        const streamCtx = this.ui.startStreamingMessage();
        this.ui.showReasoningPending(streamCtx);

        // Reasoning token'larını biriktiren buffer
        const reasoningState = { buffer: "" };
        const abortController = new AbortController();

        try {
            await this.api.sendMessageStream(
                text,
                (evt) => this._handleStreamEvent(streamCtx, reasoningState, evt),
                abortController.signal
            );

            this.ui.finalizeStreamingMessage(streamCtx);
            this.messageCount++;
            this.refreshSidebar();

            // 2+ mesaj çifti sonra rating widget'ını göster (henüz gösterilmemişse)
            this._maybeShowRating();
        } catch (error) {
            this.ui.finalizeStreamingMessage(streamCtx);
            this.ui.addMessage("error",
                `Bağlantı hatası: ${error.message}\n\n` +
                `API'nin http://localhost:5021 üzerinde çalıştığından emin olun.`);
        } finally {
            this.ui.setInputEnabled(true);
            this.ui.focusInput();
        }
    }

    /**
     * 2+ mesaj çifti (4+ mesaj) sonra rating widget'ını gösterir.
     * Session başına tek sefer gösterilir. Admin sohbeti aktifken
     * bastırılır — sohbet kapandıktan sonra (human_left) tekrar denenir.
     */
    _maybeShowRating() {
        if (this.ratingShown) return;
        if (this.messageCount < 4) return;
        if (!this.api.sessionId) return;
        // Admin ile aktif sohbet sırasında puanlama gösterme
        if (this.humanModeActive) return;

        this.ratingShown = true;
        const sessionId = this.api.sessionId;

        this.ui.showRatingWidget(async (stars, feedback) => {
            await this.api.submitRating(sessionId, stars, feedback);
        });
    }

    _handleStreamEvent(streamCtx, reasoningState, evt) {
        const { type, data } = evt;

        switch (type) {
            case "session":
                // Session ID API client'ta set edildi — persistent SSE'yi aç
                if (data?.sessionId) {
                    this._ensurePersistentEvents(data.sessionId);
                }
                break;

            case "reasoning_start":
                reasoningState.buffer = "";
                this.ui.showReasoningPending(streamCtx);
                break;

            case "reasoning_delta":
                // Token'ı buffer'a ekle ve progressive parse et
                if (data?.text) {
                    reasoningState.buffer += data.text;
                    this.ui.updateReasoningProgress(streamCtx, reasoningState.buffer);
                }
                break;

            case "reasoning_complete":
                reasoningState.buffer = "";
                this.ui.setReasoning(streamCtx, data);
                break;

            case "agent":
                this.ui.setAgentStatus(streamCtx, data?.name, data?.status, data);
                break;

            case "response_start":
                this.ui.hideAgentStatus(streamCtx);
                break;

            case "response_delta":
                if (data?.text) {
                    this.ui.appendResponseChunk(streamCtx, data.text);
                }
                break;

            case "response_complete":
                // Nihai metin — delta'lar zaten biriktirdi
                break;

            case "error":
                this.ui.appendResponseChunk(streamCtx,
                    `\n\n⚠️ ${data?.message || "Bilinmeyen hata"}`);
                break;

            case "done":
                // Stream tamamlandı
                break;

            // ─── HITL Live Takeover events (per-request /chat/stream) ───
            // Not: admin mesajları persistent /chat/events üzerinden gelir.
            // Burada sadece /chat/stream'in kendi ilettiği human_joined'ı
            // Handle ediyoruz — boş bot iskeletini kaldırmak için.
            case "human_joined":
                if (streamCtx?.messageDiv && streamCtx.messageDiv.parentNode) {
                    streamCtx.messageDiv.parentNode.removeChild(streamCtx.messageDiv);
                }
                this.ui.setHumanMode(true, data?.humanAgent);
                this.humanModeActive = true;
                break;

            case "human_message":
            case "human_left":
                // Persistent /chat/events kanalı iletir; burada no-op.
                break;

            // ─── Sentiment events ───
            // Sentiment verileri yalnızca admin panelinde gösterilir;
            // müşteri tarafında no-op.
            case "sentiment_update":
            case "sentiment_alert":
                break;
        }
    }
}

// ─── UYGULAMA BAŞLATMA ───
document.addEventListener("DOMContentLoaded", () => {
    const app = new ChatApp("http://localhost:5021");
    app.init();
});
