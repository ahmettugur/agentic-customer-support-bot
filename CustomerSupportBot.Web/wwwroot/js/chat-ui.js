// Js/chat-ui.js
// DOM manipülasyonu ve UI bileşenleri

class ChatUI {
    messagesEl;
    welcomeEl;
    inputEl;
    sendBtn;
    newChatBtn;

    // SVG ikon sabitleri
    static USER_ICON = '<img src="images/user.png" alt="Kullanıcı" class="avatar-img" />';
    static BOT_ICON = '<img src="images/chatbot.png" alt="Asistan" class="avatar-img" />';
    static ERROR_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" x2="12" y1="8" y2="12"/><line x1="12" x2="12.01" y1="16" y2="16"/></svg>';
    static CHAT_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7.9 20A9 9 0 1 0 4 16.1L2 22z"/></svg>';
    static BRAIN_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5a3 3 0 1 0-5.997.125 4 4 0 0 0-2.526 5.77 4 4 0 0 0 .556 6.588A4 4 0 1 0 12 18Z"/><path d="M12 5a3 3 0 1 1 5.997.125 4 4 0 0 1 2.526 5.77 4 4 0 0 1-.556 6.588A4 4 0 1 1 12 18Z"/><path d="M15 13a4.5 4.5 0 0 1-3-4"/><path d="M17.599 6.5a3 3 0 0 0 .399-1.375"/><path d="M6.003 5.125A3 3 0 0 0 6.401 6.5"/><path d="M3.477 10.896a4 4 0 0 1 .585-.396"/><path d="M19.938 10.5a4 4 0 0 1 .585.396"/><path d="M6 18a4 4 0 0 1-1.967-.516"/><path d="M19.967 17.484A4 4 0 0 1 18 18"/></svg>';
    static CHEVRON_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>';

    constructor() {
        this.messagesEl = document.getElementById("messages");
        this.welcomeEl = document.getElementById("welcome");
        this.inputEl = document.getElementById("userInput");
        this.sendBtn = document.getElementById("sendBtn");
        this.newChatBtn = document.getElementById("newChatBtn");
    }

    get inputText() {
        return this.inputEl.value.trim();
    }

    clearInput() {
        this.inputEl.value = "";
    }

    focusInput() {
        this.inputEl.focus();
    }

    setInputEnabled(enabled) {
        this.inputEl.disabled = !enabled;
        this.sendBtn.disabled = !enabled;
    }

    addMessage(role, text, reasoning = null) {
        // İlk mesajda welcome ekranını gizle
        if (this.welcomeEl && this.welcomeEl.parentNode) {
            this.welcomeEl.remove();
            this.welcomeEl = null;
        }

        const messageDiv = document.createElement("div");
        messageDiv.className = `message ${role}`;

        const avatar = document.createElement("div");
        avatar.className = "message-avatar";
        if (role === "user") {
            avatar.innerHTML = ChatUI.USER_ICON;
        } else if (role === "error") {
            avatar.innerHTML = ChatUI.ERROR_ICON;
        } else {
            avatar.innerHTML = ChatUI.BOT_ICON;
        }

        const content = document.createElement("div");
        content.className = "message-content";

        // Reasoning paneli (varsa bot mesajının üstüne ekle)
        if (role === "bot" && reasoning && this.hasReasoningContent(reasoning)) {
            content.appendChild(this.buildReasoningPanel(reasoning));
        }

        const bubble = document.createElement("div");
        bubble.className = "message-bubble";
        if (role === "bot") {
            bubble.classList.add("md");
            bubble.innerHTML = this.renderMarkdown(text);
        } else {
            bubble.textContent = text;
        }
        content.appendChild(bubble);

        messageDiv.appendChild(avatar);
        messageDiv.appendChild(content);
        this.messagesEl.appendChild(messageDiv);

        this.scrollToBottom();
        return messageDiv;
    }

    hasReasoningContent(reasoning) {
        return reasoning && (
            (reasoning.analysis && reasoning.analysis.trim().length > 0) ||
            (reasoning.steps && reasoning.steps.length > 0)
        );
    }

    /**
     * Step listesinden tek bir elemanın gösterim metnini alır.
     * sonrası: step artık yapılandırılmış object (description + action +
     * grounding + confidence). Geriye dönük uyumluluk için string de destekler.
     */
    getStepText(step) {
        if (!step) return "";
        if (typeof step === "string") return step;
        if (typeof step === "object") {
            return step.description || step.step || step.label || JSON.stringify(step);
        }
        return String(step);
    }

    /**
     * Yapılandırılmış step'in <li> içeriğini (wrapper'sız) üretir.
     * innerHTML atamalarında kullanılır.
     */
    renderStepInnerHtml(step) {
        const desc = this.escapeHtml(this.getStepText(step));
        if (!step || typeof step === "string") {
            return desc;
        }
        const chips = [];
        if (step.grounding) {
            const cls = step.grounding === "assumption" ? "step-chip warn" : "step-chip";
            chips.push(`<span class="${cls}">${this.escapeHtml(step.grounding)}</span>`);
        }
        if (typeof step.confidence === "number") {
            const pct = Math.round(step.confidence * 100);
            chips.push(`<span class="step-chip conf">${pct}%</span>`);
        }
        if (step.action) {
            chips.push(`<span class="step-chip action">${this.escapeHtml(step.action)}</span>`);
        }
        const chipHtml = chips.length ? `<span class="step-chips">${chips.join("")}</span>` : "";
        return `<span class="step-text">${desc}</span>${chipHtml}`;
    }

    /**
     * Yapılandırılmış step için <li>...</li> wrapper'lı HTML.
     * buildReasoningPanel'in stepsHtml.join() kullanımı için.
     */
    renderStepHtml(step) {
        return `<li>${this.renderStepInnerHtml(step)}</li>`;
    }

    buildReasoningPanel(reasoning, open = true) {
        const panel = document.createElement("details");
        panel.className = "reasoning-panel";
        if (open) panel.setAttribute("open", "");

        const summary = document.createElement("summary");
        summary.className = "reasoning-summary";
        summary.innerHTML = `
            <span class="reasoning-icon">${ChatUI.BRAIN_ICON}</span>
            <span class="reasoning-label">Düşünce süreci</span>
            <span class="reasoning-badge">${reasoning.confidence || "orta"}</span>
            <span class="reasoning-chevron">${ChatUI.CHEVRON_ICON}</span>
        `;
        panel.appendChild(summary);

        const body = document.createElement("div");
        body.className = "reasoning-body";

        // Analiz
        if (reasoning.analysis) {
            const analysisEl = document.createElement("div");
            analysisEl.className = "reasoning-section";
            analysisEl.innerHTML = `
                <div class="reasoning-section-title">Analiz</div>
                <div class="reasoning-section-text">${this.escapeHtml(reasoning.analysis)}</div>
            `;
            body.appendChild(analysisEl);
        }

        // Adımlar — yapılandırılmış step object'leri (legacy string de desteklenir)
        if (reasoning.steps && reasoning.steps.length > 0) {
            const stepsEl = document.createElement("div");
            stepsEl.className = "reasoning-section";
            const stepsHtml = reasoning.steps
                .map(s => this.renderStepHtml(s))
                .join("");
            stepsEl.innerHTML = `
                <div class="reasoning-section-title">Adımlar</div>
                <ol class="reasoning-steps">${stepsHtml}</ol>
            `;
            body.appendChild(stepsEl);
        }

        // Meta bilgiler
        const meta = document.createElement("div");
        meta.className = "reasoning-meta";
        const metaParts = [];
        if (reasoning.intent && reasoning.intent !== "bilinmiyor") {
            metaParts.push(`<span class="reasoning-tag">Niyet: ${this.escapeHtml(reasoning.intent)}</span>`);
        }
        if (reasoning.requiredInfo && reasoning.requiredInfo.length > 0) {
            metaParts.push(`<span class="reasoning-tag">Gerekli: ${reasoning.requiredInfo.map(r => this.escapeHtml(r)).join(", ")}</span>`);
        }
        if (metaParts.length > 0) {
            meta.innerHTML = metaParts.join("");
            body.appendChild(meta);
        }

        // SubTasks (compound query decomposition)
        if (reasoning.subTasks && reasoning.subTasks.length >= 2) {
            const subEl = document.createElement("div");
            subEl.className = "reasoning-section reasoning-subtasks";
            const subHtml = reasoning.subTasks
                .map(st => {
                    const agent = this.escapeHtml(st.targetAgent || "?");
                    const desc = this.escapeHtml(st.description || "");
                    const entries = st.entities ? Object.entries(st.entities) : [];
                    const entChips = entries
                        .map(([k, v]) => `<span class="subtask-entity">${this.escapeHtml(k)}=${this.escapeHtml(String(v))}</span>`)
                        .join("");
                    return `
                        <li class="subtask-item">
                            <span class="subtask-order">#${st.order || "?"}</span>
                            <span class="subtask-agent">${agent}</span>
                            <span class="subtask-desc">${desc}</span>
                            ${entChips ? `<span class="subtask-entities">${entChips}</span>` : ""}
                        </li>`;
                })
                .join("");
            subEl.innerHTML = `
                <div class="reasoning-section-title">Alt görevler <span class="subtask-count">${reasoning.subTasks.length}</span></div>
                <ul class="subtask-list">${subHtml}</ul>
            `;
            body.appendChild(subEl);
        }

        // Sanity issues (tutarsızlık uyarıları)
        if (reasoning.sanityIssues && reasoning.sanityIssues.length > 0) {
            const issuesEl = document.createElement("div");
            issuesEl.className = "reasoning-section reasoning-issues";
            const issuesHtml = reasoning.sanityIssues
                .map(issue => {
                    const sev = (issue.severity || "info").toString().toLowerCase();
                    const sevBadge = sev === "error" ? "⛔" : (sev === "warn" ? "⚠️" : "ℹ️");
                    const fix = issue.suggestedFix
                        ? `<div class="issue-fix">${this.escapeHtml(issue.suggestedFix)}</div>`
                        : "";
                    return `
                        <li class="issue-item issue-${sev}">
                            <div class="issue-header">
                                <span class="issue-sev">${sevBadge}</span>
                                <span class="issue-code">${this.escapeHtml(issue.code || "")}</span>
                            </div>
                            <div class="issue-message">${this.escapeHtml(issue.message || "")}</div>
                            ${fix}
                        </li>`;
                })
                .join("");
            issuesEl.innerHTML = `
                <div class="reasoning-section-title">Tutarsızlık kontrolleri</div>
                <ul class="issue-list">${issuesHtml}</ul>
            `;
            body.appendChild(issuesEl);
        }

        panel.appendChild(body);
        return panel;
    }

    escapeHtml(str) {
        const div = document.createElement("div");
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Bot mesajını markdown'dan HTML'e dönüştürür.
     * marked + DOMPurify CDN'den yüklü değilse plain-text fallback yapar.
     */
    renderMarkdown(text) {
        const safe = text == null ? "" : String(text);
        if (typeof window.marked === "undefined" || typeof window.DOMPurify === "undefined") {
            return this.escapeHtml(safe).replace(/\n/g, "<br>");
        }
        try {
            const html = window.marked.parse(safe, {
                breaks: true,
                gfm: true,
                async: false
            });
            return window.DOMPurify.sanitize(html, {
                USE_PROFILES: { html: true }
            });
        } catch {
            return this.escapeHtml(safe).replace(/\n/g, "<br>");
        }
    }

    clearMessages() {
        this.messagesEl.innerHTML = "";
        this.welcomeEl = null;
    }

    // ─── STREAMING ───

    /**
     * Streaming bot mesajı için iskelet oluşturur.
     * Reasoning panel + agent status chip + boş bubble + blinking cursor içerir.
     * Dönen obje append/update metodlarıyla manipüle edilir.
     */
    startStreamingMessage() {
        if (this.welcomeEl && this.welcomeEl.parentNode) {
            this.welcomeEl.remove();
            this.welcomeEl = null;
        }

        const messageDiv = document.createElement("div");
        messageDiv.className = "message bot streaming";

        const avatar = document.createElement("div");
        avatar.className = "message-avatar";
        avatar.innerHTML = ChatUI.BOT_ICON;

        const content = document.createElement("div");
        content.className = "message-content";

        // Reasoning panel placeholder (data geldiğinde doldurulacak)
        const reasoningPlaceholder = document.createElement("div");
        reasoningPlaceholder.className = "reasoning-placeholder";
        content.appendChild(reasoningPlaceholder);

        // Agent status chip (aktif ajan göstergesi)
        const agentChip = document.createElement("div");
        agentChip.className = "agent-chip hidden";
        content.appendChild(agentChip);

        // Response bubble
        const bubble = document.createElement("div");
        bubble.className = "message-bubble streaming-bubble";

        const textSpan = document.createElement("span");
        textSpan.className = "streaming-text";
        bubble.appendChild(textSpan);

        const cursor = document.createElement("span");
        cursor.className = "streaming-cursor";
        bubble.appendChild(cursor);

        content.appendChild(bubble);
        messageDiv.appendChild(avatar);
        messageDiv.appendChild(content);
        this.messagesEl.appendChild(messageDiv);

        this.scrollToBottom();

        return {
            messageDiv,
            reasoningPlaceholder,
            agentChip,
            bubble,
            textSpan,
            cursor,
            text: "",
        };
    }

    /** Response delta'yı bubble'a ekler. */
    appendResponseChunk(streamCtx, chunk) {
        streamCtx.text += chunk;
        streamCtx.textSpan.textContent = streamCtx.text;
        this.scrollToBottom();
    }

    /** Reasoning complete olduğunda panel'i tamamen parse edilmiş haliyle doldurur. */
    setReasoning(streamCtx, reasoning) {
        if (!this.hasReasoningContent(reasoning)) return;

        // Streaming panel zaten varsa in-place güncelle - DOM değişikliğini
        // Minimize ederek smooth geçiş sağla
        const existingPanel = streamCtx.reasoningPlaceholder.querySelector(
            ".reasoning-panel.streaming");
        if (existingPanel) {
            this._finalizeStreamingReasoningPanel(existingPanel, reasoning);
            this.scrollToBottom();
            return;
        }

        // Streaming panel yoksa (ör. tek delta'da tam geldi) fresh panel oluştur
        streamCtx.reasoningPlaceholder.innerHTML = "";
        streamCtx.reasoningPlaceholder.appendChild(this.buildReasoningPanel(reasoning));
        this.scrollToBottom();
    }

    /** Streaming reasoning panelini final haline dönüştürür (smooth). */
    _finalizeStreamingReasoningPanel(panel, reasoning) {
        // Streaming class'ı kaldır - mor glow gider, tam yeşil/mor'a geçer
        panel.classList.remove("streaming");

        // Summary'i güncelle - "Düşünüyor" yerine "Düşünce süreci"
        const summary = panel.querySelector(".reasoning-summary");
        if (summary) {
            summary.innerHTML = `
                <span class="reasoning-icon">${ChatUI.BRAIN_ICON}</span>
                <span class="reasoning-label">Düşünce süreci</span>
                <span class="reasoning-badge">${this.escapeHtml(reasoning.confidence || "orta")}</span>
                <span class="reasoning-chevron">${ChatUI.CHEVRON_ICON}</span>
            `;
        }

        // Analysis text'i final değerle güncelle (zaten yakın olmalı)
        const analysisEl = panel.querySelector(".reasoning-section-text");
        if (analysisEl && reasoning.analysis) {
            analysisEl.textContent = reasoning.analysis;
        }

        // Steps listesini final değerle senkronize et
        const stepsList = panel.querySelector(".reasoning-steps");
        if (stepsList && reasoning.steps) {
            const currentCount = stepsList.children.length;
            const finalSteps = reasoning.steps;

            // Fazla adımları kaldır
            while (stepsList.children.length > finalSteps.length) {
                stepsList.removeChild(stepsList.lastChild);
            }

            // Mevcut adımları güncelle — zengin HTML (description + chip'ler)
            for (let i = 0; i < Math.min(currentCount, finalSteps.length); i++) {
                stepsList.children[i].innerHTML = this.renderStepInnerHtml(finalSteps[i]);
                stepsList.children[i].classList.remove("reasoning-step-new");
            }

            // Yeni adımları ekle
            for (let i = currentCount; i < finalSteps.length; i++) {
                const li = document.createElement("li");
                li.innerHTML = this.renderStepInnerHtml(finalSteps[i]);
                li.className = "reasoning-step-new";
                stepsList.appendChild(li);
            }
        }

        // Body'ye meta tag'leri ekle (intent + requiredInfo)
        const body = panel.querySelector(".reasoning-body");
        if (body && !body.querySelector(".reasoning-meta")) {
            const metaParts = [];
            if (reasoning.intent && reasoning.intent !== "bilinmiyor") {
                metaParts.push(`<span class="reasoning-tag">Niyet: ${this.escapeHtml(reasoning.intent)}</span>`);
            }
            if (reasoning.requiredInfo && reasoning.requiredInfo.length > 0) {
                metaParts.push(`<span class="reasoning-tag">Gerekli: ${reasoning.requiredInfo.map(r => this.escapeHtml(r)).join(", ")}</span>`);
            }
            if (metaParts.length > 0) {
                const meta = document.createElement("div");
                meta.className = "reasoning-meta";
                meta.innerHTML = metaParts.join("");
                body.appendChild(meta);
            }
        }
    }

    /**
     * Reasoning streaming sırasında accumulating JSON buffer'ından
     * analysis + steps'i çıkarıp canlı olarak günceller.
     * Tam JSON gelmeden önce de kısmi görünüm sağlar.
     */
    updateReasoningProgress(streamCtx, buffer) {
        const partial = ChatUI.parsePartialReasoningJson(buffer);

        // Henüz gösterilebilir içerik yok — spinner'ı göster
        if (!partial.analysis && (!partial.steps || partial.steps.length === 0)) {
            this.showReasoningPending(streamCtx);
            return;
        }

        // Kısmi panel'i oluştur/güncelle
        let panel = streamCtx.reasoningPlaceholder.querySelector(".reasoning-panel.streaming");
        if (!panel) {
            streamCtx.reasoningPlaceholder.innerHTML = "";
            panel = this.buildStreamingReasoningPanel();
            streamCtx.reasoningPlaceholder.appendChild(panel);
        }

        // Analysis güncelle
        const analysisEl = panel.querySelector(".reasoning-section-text");
        if (analysisEl) {
            analysisEl.textContent = partial.analysis || "…";
        }

        // Steps güncelle — streaming sırasında object'ler henuz eksik olabilir,
        // O yüzden getStepText ile güvenli çekim + textContent kullanıyoruz (no chips).
        const stepsListEl = panel.querySelector(".reasoning-steps");
        if (stepsListEl) {
            const currentCount = stepsListEl.children.length;
            const steps = partial.steps || [];
            // Yeni adımları ekle
            for (let i = currentCount; i < steps.length; i++) {
                const li = document.createElement("li");
                li.textContent = this.getStepText(steps[i]);
                li.className = "reasoning-step-new";
                stepsListEl.appendChild(li);
            }
            // Son adımı update et (henüz tam gelmemiş olabilir)
            if (steps.length > 0 && steps.length <= stepsListEl.children.length) {
                const lastLi = stepsListEl.children[steps.length - 1];
                lastLi.textContent = this.getStepText(steps[steps.length - 1]);
            }
        }

        this.scrollToBottom();
    }

    /** Streaming sırasında kullanılan canlı reasoning paneli iskeleti. */
    buildStreamingReasoningPanel() {
        const panel = document.createElement("details");
        panel.className = "reasoning-panel streaming";
        panel.setAttribute("open", "");

        panel.innerHTML = `
            <summary class="reasoning-summary">
                <span class="reasoning-icon">${ChatUI.BRAIN_ICON}</span>
                <span class="reasoning-label">Düşünüyor</span>
                <span class="reasoning-dots inline"><span></span><span></span><span></span></span>
                <span class="reasoning-chevron">${ChatUI.CHEVRON_ICON}</span>
            </summary>
            <div class="reasoning-body">
                <div class="reasoning-section">
                    <div class="reasoning-section-title">Analiz</div>
                    <div class="reasoning-section-text">…</div>
                </div>
                <div class="reasoning-section">
                    <div class="reasoning-section-title">Adımlar</div>
                    <ol class="reasoning-steps"></ol>
                </div>
            </div>
        `;
        return panel;
    }

    /** Reasoning henüz herhangi bir içerik üretmediğinde gösterilir. */
    showReasoningPending(streamCtx) {
        if (streamCtx.reasoningPlaceholder.children.length > 0) return;
        const pending = document.createElement("div");
        pending.className = "reasoning-pending";
        pending.innerHTML = `
            <span class="reasoning-icon">${ChatUI.BRAIN_ICON}</span>
            <span>Düşünüyor</span>
            <span class="reasoning-dots"><span></span><span></span><span></span></span>
        `;
        streamCtx.reasoningPlaceholder.appendChild(pending);
        this.scrollToBottom();
    }

    /**
     * Eksik/tamamlanmamış JSON buffer'ından analysis, steps, intent gibi
     * alanları regex ile çıkarır. Bir alan yarı gelmişse son tam olanı kullanır.
     */
    static parsePartialReasoningJson(buffer) {
        if (!buffer) return {};

        // Önce tam parse'ı dene — fence'leri strip et
        const cleaned = ChatUI._stripJsonFences(buffer);
        try {
            return JSON.parse(cleaned);
        } catch {
            // Fall through to regex-based partial extraction
        }

        return {
            analysis: ChatUI._extractJsonString(cleaned, "analysis"),
            steps: ChatUI._extractJsonStringArray(cleaned, "steps"),
            intent: ChatUI._extractJsonString(cleaned, "intent"),
            confidence: ChatUI._extractJsonString(cleaned, "confidence") || "analiz ediliyor"
        };
    }

    static _stripJsonFences(text) {
        return text
            .replace(/^```json\s*/i, "")
            .replace(/^```\s*/, "")
            .replace(/```\s*$/, "")
            .trim();
    }

    static _extractJsonString(buffer, key) {
        // "key" : "value" şeklindeki alanı yakala — escape'li tırnakları destekler
        const regex = new RegExp(`"${key}"\\s*:\\s*"((?:\\\\.|[^"\\\\])*)"`, "s");
        const match = buffer.match(regex);
        if (match) return ChatUI._unescapeJsonString(match[1]);

        // Yarım gelmiş: "key": "xxx (kapanış tırnağı yok)
        const partialRegex = new RegExp(`"${key}"\\s*:\\s*"((?:\\\\.|[^"\\\\])*)$`, "s");
        const partialMatch = buffer.match(partialRegex);
        return partialMatch ? ChatUI._unescapeJsonString(partialMatch[1]) : null;
    }

    static _extractJsonStringArray(buffer, key) {
        const arrayStartRegex = new RegExp(`"${key}"\\s*:\\s*\\[`, "s");
        const startMatch = buffer.match(arrayStartRegex);
        if (!startMatch) return [];

        const arrayStart = startMatch.index + startMatch[0].length;
        const items = [];
        let i = arrayStart;

        while (i < buffer.length) {
            // Whitespace atla
            while (i < buffer.length && /[\s,]/.test(buffer[i])) i++;
            if (i >= buffer.length) break;
            if (buffer[i] === "]") break;
            if (buffer[i] !== '"') break;

            // String başladı, escape'leri destekleyerek sonunu bul
            i++; // Tırnak içindeyiz
            let str = "";
            let escaped = false;
            let closed = false;
            while (i < buffer.length) {
                const c = buffer[i];
                if (escaped) {
                    str += c;
                    escaped = false;
                } else if (c === "\\") {
                    escaped = true;
                    str += c;
                } else if (c === '"') {
                    closed = true;
                    i++;
                    break;
                } else {
                    str += c;
                }
                i++;
            }
            items.push(ChatUI._unescapeJsonString(str));
            if (!closed) break; // Yarım string — ekle ve çık
        }

        return items;
    }

    static _unescapeJsonString(s) {
        return s
            .replace(/\\"/g, '"')
            .replace(/\\n/g, '\n')
            .replace(/\\t/g, '\t')
            .replace(/\\r/g, '\r')
            .replace(/\\\\/g, '\\');
    }

    /** Agent status chip'i günceller. */
    setAgentStatus(streamCtx, agentName, status, extra) {
        const friendlyName = this.friendlyAgentName(agentName);
        const parallel = !!(extra && extra.parallel);
        const badge = parallel
            ? '<span class="agent-chip-badge">paralel</span>'
            : '';
        if (status === "running") {
            streamCtx.agentChip.className = "agent-chip running";
            streamCtx.agentChip.innerHTML = `
                <span class="agent-chip-dot"></span>
                <span class="agent-chip-text">${this.escapeHtml(friendlyName)} çalışıyor…</span>
                ${badge}
            `;
        } else if (status === "done") {
            streamCtx.agentChip.className = "agent-chip done";
            streamCtx.agentChip.innerHTML = `
                <span class="agent-chip-check">✓</span>
                <span class="agent-chip-text">${this.escapeHtml(friendlyName)} tamamlandı</span>
                ${badge}
            `;
        } else if (status === "decomposing") {
            streamCtx.agentChip.className = "agent-chip running";
            const total = (extra && extra.subTaskCount) || "?";
            const pg = (extra && extra.parallelGroups) || 0;
            streamCtx.agentChip.innerHTML = `
                <span class="agent-chip-dot"></span>
                <span class="agent-chip-text">Orkestratör: ${this.escapeHtml(String(total))} alt görev—${pg} paralel grup</span>
            `;
        } else if (status === "aggregating") {
            streamCtx.agentChip.className = "agent-chip running";
            streamCtx.agentChip.innerHTML = `
                <span class="agent-chip-dot"></span>
                <span class="agent-chip-text">Sonuçlar birleştiriliyor…</span>
            `;
        }
    }

    hideAgentStatus(streamCtx) {
        streamCtx.agentChip.className = "agent-chip hidden";
    }

    /** Streaming bittiğinde cursor'u kaldırır ve streaming class'ını temizler. */
    finalizeStreamingMessage(streamCtx) {
        streamCtx.messageDiv.classList.remove("streaming");
        streamCtx.bubble.classList.remove("streaming-bubble");
        streamCtx.cursor.remove();
        // Markdown render: streaming sırasında tokenler text olarak akar,
        // mesaj tamamlandığında md formatına çevirip bubble'ı yeniden çiziyoruz.
        if (streamCtx.text && streamCtx.text.length > 0) {
            streamCtx.bubble.classList.add("md");
            streamCtx.bubble.innerHTML = this.renderMarkdown(streamCtx.text);
        }
        this.hideAgentStatus(streamCtx);
    }

    friendlyAgentName(id) {
        const map = {
            "PlanningAgent": "Planlama Ajanı",
            "ProductInquiryAgent": "Ürün Ajanı",
            "OrderPlacementAgent": "Sipariş Ajanı",
            "OrderInquiryAgent": "Sipariş Sorgu Ajanı",
            "ComplaintAgent": "Şikayet Ajanı",
            "ResponseAgent": "Yanıt Ajanı",
            "Orchestrator": "Orkestratör"
        };
        if (id && id.startsWith("SubTask#")) {
            return `Alt Görev ${id.substring(8)}`;
        }
        // MAF executor ID'leri "PlanningAgent_<hash>" formatında olabilir
        for (const key of Object.keys(map)) {
            if (id && id.startsWith(key)) return map[key];
        }
        return id || "Asistan";
    }

    showTypingIndicator() {
        const messageDiv = document.createElement("div");
        messageDiv.className = "message bot";
        messageDiv.id = "typing";

        const avatar = document.createElement("div");
        avatar.className = "message-avatar";
        avatar.innerHTML = ChatUI.BOT_ICON;

        const bubble = document.createElement("div");
        bubble.className = "message-bubble";
        bubble.innerHTML = '<div class="typing-indicator"><span></span><span></span><span></span></div>';

        messageDiv.appendChild(avatar);
        messageDiv.appendChild(bubble);
        this.messagesEl.appendChild(messageDiv);

        this.scrollToBottom();
    }

    hideTypingIndicator() {
        const typing = document.getElementById("typing");
        if (typing) typing.remove();
    }


    // ─── EVENT BINDING ───

    onSend(callback) {
        this.sendBtn.addEventListener("click", () => callback());

        this.inputEl.addEventListener("keypress", (e) => {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                callback();
            }
        });
    }

    onExampleChipClick(callback) {
        document.querySelectorAll(".example-chip").forEach(chip => {
            chip.addEventListener("click", () => {
                const text = chip.getAttribute("data-text");
                callback(text);
            });
        });
    }

    onNewChat(callback) {
        if (this.newChatBtn) {
            this.newChatBtn.addEventListener("click", () => callback());
        }
    }

    scrollToBottom() {
        this.messagesEl.scrollTop = this.messagesEl.scrollHeight;
    }

    // ─── HITL Live Takeover ───

    /**
     * "Müşteri temsilcisi katıldı/ayrıldı" gibi sistem bildirimi ekler (sarı pill).
     */
    addSystemNotice(text) {
        if (this.welcomeEl && this.welcomeEl.parentNode) {
            this.welcomeEl.remove();
            this.welcomeEl = null;
        }
        const div = document.createElement("div");
        div.className = "system-notice";
        div.textContent = text;
        this.messagesEl.appendChild(div);
        this.scrollToBottom();
    }

    /**
     * İnsan temsilcinin yazdığı mesajı gösterir (yeşil bubble + agent ismi).
     */
    addHumanMessage(text, humanAgent) {
        if (this.welcomeEl && this.welcomeEl.parentNode) {
            this.welcomeEl.remove();
            this.welcomeEl = null;
        }

        const messageDiv = document.createElement("div");
        messageDiv.className = "message human";

        const avatar = document.createElement("div");
        avatar.className = "message-avatar";
        avatar.innerHTML = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>';

        const content = document.createElement("div");
        content.className = "message-content";

        const name = document.createElement("div");
        name.className = "human-agent-label";
        name.textContent = `🟢 ${humanAgent || "Temsilci"}`;
        content.appendChild(name);

        const bubble = document.createElement("div");
        bubble.className = "message-bubble human-bubble";
        bubble.textContent = text;
        content.appendChild(bubble);

        messageDiv.appendChild(avatar);
        messageDiv.appendChild(content);
        this.messagesEl.appendChild(messageDiv);
        this.scrollToBottom();
    }

    /**
     * Banner: "Bir müşteri temsilcisi sohbete katıldı" / kaldır.
     * Yeşil banner — human modda aktif.
     * Bu banner açıldığında pending banner da otomatik olarak kalkar.
     */
    setHumanMode(active, humanAgent) {
        const existing = document.getElementById("humanModeBanner");
        if (existing) existing.remove();

        if (active) {
            // Human mode açıldıysa pending banner'ı temizle (varsa)
            this.setPendingHandoff(false);

            const banner = document.createElement("div");
            banner.id = "humanModeBanner";
            banner.className = "human-mode-banner";
            banner.innerHTML = `
                <span class="dot-live"></span>
                <strong>${humanAgent || "Müşteri temsilcisi"}</strong> şu an sohbette.
                Bot yerine size <em>insan</em> bir temsilci yanıt veriyor.
            `;
            const main = document.querySelector(".chat-main") || this.messagesEl.parentNode;
            main.insertBefore(banner, this.messagesEl);
        }
    }

    /**
     * Pending handoff banner'ı: "Bir temsilci bağlanıyor, lütfen bekleyin" (sarı).
     * Admin henüz takeover yapmadı, ama eskalasyon oluştu. Input da kilitlenir
     * çünkü kullanıcının bu durumda yeni mesaj yazması duplicate eskalasyon
     * doğurur ve kafa karıştırır.
     *
     * @param {boolean} active
     * @param {string=} reason  Opsiyonel sebep (eskalasyon kaydından gelir)
     */
    setPendingHandoff(active, reason) {
        const existing = document.getElementById("pendingHandoffBanner");
        if (existing) existing.remove();

        if (active) {
            const banner = document.createElement("div");
            banner.id = "pendingHandoffBanner";
            banner.className = "human-mode-banner pending";
            banner.innerHTML = `
                <span class="dot-pending"></span>
                <span class="pending-text">
                    <strong>Bir müşteri temsilcisi bağlanıyor…</strong>
                    Lütfen yanıt gelene kadar bekleyin.
                    ${reason ? `<span class="pending-reason" title="${this._escapeAttr(reason)}"></span>` : ""}
                </span>
            `;
            const main = document.querySelector(".chat-main") || this.messagesEl.parentNode;
            main.insertBefore(banner, this.messagesEl);

            // Input'u kilitle + placeholder güncelle
            this._lockInputForHandoff(true);
        } else {
            // Pending kaldırılırken input'u da serbest bırak
            // (human mode aktifse setHumanMode + ayrı bir lock uygular, dokunmayız)
            const stillHuman = !!document.getElementById("humanModeBanner");
            if (!stillHuman) {
                this._lockInputForHandoff(false);
            }
        }
    }

    /**
     * Input'u handoff için kilitle / çöz. Placeholder'ı değiştirerek sebebi
     * netleştirir. `setInputEnabled`'dan ayrı tutuyoruz; normal "yanıt streaming
     * sırasında disabled" mantığı bozulmaz.
     */
    _lockInputForHandoff(lock) {
        if (!this.inputEl) return;
        if (lock) {
            this._prevPlaceholder = this.inputEl.placeholder;
            this.inputEl.placeholder = "Temsilci bağlanana kadar lütfen bekleyin…";
            this.inputEl.disabled = true;
            if (this.sendBtn) this.sendBtn.disabled = true;
        } else {
            if (this._prevPlaceholder !== undefined) {
                this.inputEl.placeholder = this._prevPlaceholder;
                this._prevPlaceholder = undefined;
            }
            this.inputEl.disabled = false;
            if (this.sendBtn) this.sendBtn.disabled = false;
        }
    }

    _escapeAttr(s) {
        return String(s).replace(/"/g, "&quot;").replace(/</g, "&lt;");
    }

    // ─── CONVERSATION RATING WIDGET ───

    /**
     * Konuşma sonu rating widget'ını mesaj alanına ekler.
     * Kullanıcı 1-5 yıldız + opsiyonel yorum bırakabilir.
     * @param {Function} onSubmit (stars, feedback) => Promise
     */
    showRatingWidget(onSubmit) {
        // Zaten varsa tekrar gösterme
        if (document.getElementById("ratingWidget")) return;

        const widget = document.createElement("div");
        widget.id = "ratingWidget";
        widget.className = "rating-widget";
        widget.innerHTML = `
            <div class="rating-widget-inner">
                <div class="rating-widget-header">
                    <span class="rating-widget-icon">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                        </svg>
                    </span>
                    <span>Bu konuşmayı değerlendirin</span>
                </div>
                <div class="rating-stars" id="ratingStars">
                    ${[1,2,3,4,5].map(i => `
                        <button class="rating-star" data-star="${i}" title="${i} yıldız" aria-label="${i} yıldız">
                            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                            </svg>
                        </button>
                    `).join("")}
                </div>
                <div class="rating-label" id="ratingLabel">Bir puan seçin</div>
                <textarea class="rating-feedback" id="ratingFeedback" rows="2"
                    placeholder="Yorumunuz (opsiyonel)..." maxlength="500"></textarea>
                <div class="rating-actions">
                    <button class="rating-submit" id="ratingSubmitBtn" disabled>Gönder</button>
                    <button class="rating-skip" id="ratingSkipBtn">Geç</button>
                </div>
            </div>
        `;

        this.messagesEl.appendChild(widget);
        this.scrollToBottom();

        // State
        let selectedStars = 0;
        const labels = ["", "Çok Kötü", "Kötü", "Orta", "İyi", "Mükemmel"];
        const stars = widget.querySelectorAll(".rating-star");
        const label = widget.querySelector("#ratingLabel");
        const submitBtn = widget.querySelector("#ratingSubmitBtn");
        const skipBtn = widget.querySelector("#ratingSkipBtn");
        const feedback = widget.querySelector("#ratingFeedback");

        const updateStars = (count, hover = false) => {
            stars.forEach((s, i) => {
                const svg = s.querySelector("svg");
                if (i < count) {
                    svg.setAttribute("fill", "#f59e0b");
                    svg.setAttribute("stroke", "#f59e0b");
                    s.classList.add("active");
                } else {
                    svg.setAttribute("fill", "none");
                    svg.setAttribute("stroke", "currentColor");
                    s.classList.remove("active");
                }
            });
            if (!hover) {
                label.textContent = count > 0 ? `${count}/5 — ${labels[count]}` : "Bir puan seçin";
            } else {
                label.textContent = `${count}/5 — ${labels[count]}`;
            }
        };

        stars.forEach((star, i) => {
            star.addEventListener("mouseenter", () => updateStars(i + 1, true));
            star.addEventListener("mouseleave", () => updateStars(selectedStars, false));
            star.addEventListener("click", () => {
                selectedStars = i + 1;
                updateStars(selectedStars);
                submitBtn.disabled = false;
            });
        });

        submitBtn.addEventListener("click", async () => {
            if (selectedStars === 0) return;
            submitBtn.disabled = true;
            submitBtn.textContent = "Gönderiliyor...";
            try {
                await onSubmit(selectedStars, feedback.value.trim() || null);
                this._showRatingThankYou(widget, selectedStars);
            } catch (e) {
                submitBtn.disabled = false;
                submitBtn.textContent = "Gönder";
                label.textContent = "Gönderilemedi, tekrar deneyin.";
                label.style.color = "#dc2626";
            }
        });

        skipBtn.addEventListener("click", () => {
            widget.remove();
        });
    }

    /** Rating gönderildikten sonra teşekkür mesajı gösterir. */
    _showRatingThankYou(widget, stars) {
        const labels = ["", "Çok Kötü", "Kötü", "Orta", "İyi", "Mükemmel"];
        widget.innerHTML = `
            <div class="rating-widget-inner rating-thankyou">
                <div class="rating-thankyou-icon">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="#10b981" stroke="#10b981" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                        <polyline points="22 4 12 14.01 9 11.01"/>
                    </svg>
                </div>
                <div class="rating-thankyou-text">
                    Değerlendirmeniz için teşekkürler!
                    <span class="rating-thankyou-stars">${"★".repeat(stars)}${"☆".repeat(5 - stars)} — ${labels[stars]}</span>
                </div>
            </div>
        `;
        this.scrollToBottom();
    }

    /** Mevcut bir rating'i read-only olarak gösterir (session switch sırasında). */
    showExistingRating(rating) {
        if (!rating || document.getElementById("ratingWidget")) return;
        const labels = ["", "Çok Kötü", "Kötü", "Orta", "İyi", "Mükemmel"];
        const widget = document.createElement("div");
        widget.id = "ratingWidget";
        widget.className = "rating-widget";
        widget.innerHTML = `
            <div class="rating-widget-inner rating-thankyou">
                <div class="rating-thankyou-icon">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="#f59e0b" stroke="#f59e0b" stroke-width="1.5">
                        <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                    </svg>
                </div>
                <div class="rating-thankyou-text">
                    Değerlendirmeniz: ${"★".repeat(rating.stars)}${"☆".repeat(5 - rating.stars)} — ${labels[rating.stars]}
                    ${rating.feedback ? `<span class="rating-existing-feedback">"${this.escapeHtml(rating.feedback)}"</span>` : ""}
                </div>
            </div>
        `;
        this.messagesEl.appendChild(widget);
        this.scrollToBottom();
    }
}
