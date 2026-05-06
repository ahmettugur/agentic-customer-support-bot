// Js/traces.js — Session bazlı reasoning trace dashboard (class tabanlı)

class TraceDashboard {
    static API_BASE = window.location.origin;
    static REFRESH_INTERVAL_MS = 5000;

    els = {};
    sessions = [];
    allTraces = [];
    selectedSessionId = null;
    selectedTraceId = null;
    viewMode = 'session'; // 'session' | 'all'
    refreshTimer = null;

    constructor() {
        this.els = {
            sessionsList: document.getElementById('sessionsList'),
            sessionCount: document.getElementById('sessionCount'),
            showAllBtn: document.getElementById('showAllTracesBtn'),
            list: document.getElementById('tracesList'),
            detail: document.getElementById('traceDetailPane'),
            count: document.getElementById('tracesCount'),
            label: document.getElementById('tracesLabel'),
            refresh: document.getElementById('refreshBtn'),
            autoRefresh: document.getElementById('autoRefreshToggle')
        };
    }

    // ─── Init ───
    init() {
        this.els.refresh.addEventListener('click', () => this.loadSessions());
        this.els.showAllBtn.addEventListener('click', () => this.showAllTraces());
        if (this.els.autoRefresh) {
            this.els.autoRefresh.addEventListener('change', e => {
                if (e.target.checked) this.startAutoRefresh();
                else this.stopAutoRefresh();
            });
        }
        // Sayfa gizliyken boşa kaynak yakmayalım
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                this.stopAutoRefresh();
            } else if (this.els.autoRefresh?.checked) {
                this.startAutoRefresh();
                this.loadSessions(); // gizliyken biriken farklılıkları hemen çek
            }
        });

        this.loadSessions();
        if (this.els.autoRefresh?.checked) this.startAutoRefresh();
    }

    // ─── Auto-refresh ───
    startAutoRefresh() {
        this.stopAutoRefresh();
        this.refreshTimer = setInterval(() => {
            this.loadSessions();
        }, TraceDashboard.REFRESH_INTERVAL_MS);
    }

    stopAutoRefresh() {
        if (this.refreshTimer) {
            clearInterval(this.refreshTimer);
            this.refreshTimer = null;
        }
    }

    // ─── Session fetch ───
    async loadSessions() {
        try {
            const res = await window.Auth.fetch(`${TraceDashboard.API_BASE}/traces/sessions`);
            this.sessions = await res.json();
            this.els.sessionCount.textContent = this.sessions.length;
            this.renderSessionList();

            // Eğer aktif bir session seçiliyse trace'lerini de yenile
            if (this.viewMode === 'session' && this.selectedSessionId) {
                await this.loadTracesForSession(this.selectedSessionId);
            } else if (this.viewMode === 'all') {
                await this.loadAllTraces();
            }
        } catch (err) {
            this.els.sessionsList.innerHTML = `<div class="traces-empty">Hata: ${this.escapeHtml(err.message)}</div>`;
        }
    }

    // ─── Session list render ───
    renderSessionList() {
        if (!this.sessions.length) {
            this.els.sessionsList.innerHTML = '<div class="traces-empty">Henüz oturum yok. Sohbette bir mesaj gönderin.</div>';
            return;
        }

        this.els.sessionsList.innerHTML = this.sessions.map(s => {
            const time = this.formatTime(s.lastTraceAt);
            const activeCls = s.sessionId === this.selectedSessionId ? ' active' : '';
            const title = this.escapeHtml(s.title || '(boş)');
            const shortId = s.sessionId.substring(0, 8);

            return `
                <div class="session-item${activeCls}" data-session-id="${s.sessionId}">
                    <div class="session-item-title">${title}</div>
                    <div class="session-item-meta">
                        <span class="session-trace-count">${s.traceCount} trace</span>
                        <span>•</span>
                        <span>${s.messageCount} mesaj</span>
                        <span>•</span>
                        <span>${time}</span>
                    </div>
                    <code class="session-item-id">${shortId}…</code>
                </div>
            `;
        }).join('');

        document.querySelectorAll('.session-item').forEach(el => {
            el.addEventListener('click', () => this.selectSession(el.dataset.sessionId));
        });
    }

    // ─── Session selection ───
    async selectSession(sessionId) {
        this.viewMode = 'session';
        this.selectedSessionId = sessionId;
        this.selectedTraceId = null;
        this.renderSessionList(); // Update active state

        // Reset detail pane
        this.els.detail.innerHTML = `
            <div class="trace-detail-empty">
                <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" opacity="0.3">
                    <circle cx="12" cy="12" r="10"/>
                    <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/>
                    <path d="M12 17h.01"/>
                </svg>
                <p class="muted">Bir trace seçin</p>
            </div>
        `;

        await this.loadTracesForSession(sessionId);
    }

    // ─── Load traces for session ───
    async loadTracesForSession(sessionId) {
        try {
            const res = await window.Auth.fetch(`${TraceDashboard.API_BASE}/traces/by-session/${sessionId}`);
            this.allTraces = await res.json();
            this.els.count.textContent = this.allTraces.length;
            this.els.label.textContent = 'trace (oturum)';
            this.renderTraceList();
        } catch (err) {
            this.els.list.innerHTML = `<div class="traces-empty">Hata: ${this.escapeHtml(err.message)}</div>`;
        }
    }

    // ─── Show all traces (global) ───
    async showAllTraces() {
        this.viewMode = 'all';
        this.selectedSessionId = null;
        this.selectedTraceId = null;
        this.renderSessionList(); // Remove active state
        await this.loadAllTraces();
    }

    async loadAllTraces() {
        try {
            const res = await window.Auth.fetch(`${TraceDashboard.API_BASE}/traces/recent?count=50`);
            this.allTraces = await res.json();
            this.els.count.textContent = this.allTraces.length;
            this.els.label.textContent = 'trace (tümü)';
            this.renderTraceList();
        } catch (err) {
            this.els.list.innerHTML = `<div class="traces-empty">Hata: ${this.escapeHtml(err.message)}</div>`;
        }
    }

    // ─── Render trace list ───
    renderTraceList() {
        if (!this.allTraces.length) {
            const msg = this.viewMode === 'session'
                ? 'Bu oturumda henüz trace yok.'
                : 'Henüz trace yok. Sohbette bir mesaj gönderin.';
            this.els.list.innerHTML = `<div class="traces-empty">${this.escapeHtml(msg)}</div>`;
            return;
        }

        this.els.list.innerHTML = this.allTraces.map(t => {
            const time = this.formatTime(t.startedAt);
            const dur = t.durationMs != null ? `${Math.round(t.durationMs)}ms` : '—';
            const reason = t.terminationReason || '—';
            const badge = `<span class="badge badge-${this.escapeHtml(reason)}">${this.escapeHtml(reason)}</span>`;
            const query = this.escapeHtml(t.userQuery || '(boş sorgu)');
            const activeCls = t.traceId === this.selectedTraceId ? ' active' : '';

            // Global modda session bilgisini göster
            const sessionTag = this.viewMode === 'all'
                ? `<code class="trace-session-tag">${(t.sessionId || '').substring(0, 6)}…</code>`
                : '';

            return `
                <div class="trace-item${activeCls}" data-trace-id="${t.traceId}">
                    <p class="trace-item-query">${query}</p>
                    <div class="trace-item-meta">
                        ${sessionTag}
                        <span>${time}</span>
                        <span>•</span>
                        <span>${dur}</span>
                        ${badge}
                    </div>
                </div>
            `;
        }).join('');

        document.querySelectorAll('.trace-item').forEach(el => {
            el.addEventListener('click', () => this.selectTrace(el.dataset.traceId));
        });
    }

    // ─── Detail render ───
    selectTrace(traceId) {
        this.selectedTraceId = traceId;
        const trace = this.allTraces.find(t => t.traceId === traceId);
        if (!trace) return;
        this.renderTraceList(); // Update active state
        this.renderTraceDetail(trace);
    }

    renderTraceDetail(t) {
        const time = new Date(t.startedAt).toLocaleString('tr-TR');
        const dur = t.durationMs != null ? `${Math.round(t.durationMs)}ms` : '—';
        const sections = [];

        // Header
        const header = `
            <div class="trace-detail-header">
                <h2 class="trace-detail-query">${this.escapeHtml(t.userQuery || '(boş sorgu)')}</h2>
                <div class="trace-detail-meta">
                    <span>🕐 ${time}</span>
                    <span>⏱ ${dur}</span>
                    <span>🔄 ${t.iterationCount ?? 0} iter</span>
                    <span class="badge badge-${this.escapeHtml(t.terminationReason || '')}">${this.escapeHtml(t.terminationReason || '—')}</span>
                    <code>${this.escapeHtml(t.traceId)}</code>
                    <code>session: ${this.escapeHtml((t.sessionId || '').substring(0, 8))}…</code>
                    <a class="btn-secondary" style="margin-left:auto;text-decoration:none;font-size:12px;padding:4px 10px"
                       href="/replay.html?traceId=${encodeURIComponent(t.traceId)}" target="_blank">▶ Replay</a>
                </div>
            </div>
        `;

        // Global Reasoning
        if (t.reasoning) sections.push(this.renderReasoning(t.reasoning));

        // Planning
        if (t.planning) sections.push(this.renderPlanning(t.planning));

        // Specialist reasonings
        if (t.specialistReasonings?.length) sections.push(this.renderSpecialists(t.specialistReasonings));

        // Agent visits
        if (t.agentVisits?.length) sections.push(this.renderAgentVisits(t.agentVisits));

        // Final critique
        if (t.finalCritique) sections.push(this.renderFinalCritique(t.finalCritique));

        // Revision
        if (t.wasRevised) sections.push(this.renderRevision(t));

        // Final response
        if (t.finalResponse) sections.push(this.renderFinalResponse(t));

        this.els.detail.innerHTML = header + sections.join('');

        // Collapsible section headers
        document.querySelectorAll('.trace-section-header').forEach(h => {
            h.addEventListener('click', () => h.parentElement.classList.toggle('collapsed'));
        });
    }

    // ─── Section renderers ───

    renderReasoning(r) {
        return this.section('🧠 Global Reasoning', '', `
            ${this.kv('Intent', this.pill(r.intent || '—', 'pill'))}
            ${this.kv('Confidence', this.confidenceBar(r.confidenceScore ?? this.scoreFromString(r.confidence)))}
            ${this.kv('Analysis', this.escapeHtml(r.analysis || ''))}
            ${this.kv('Rationale', this.escapeHtml(r.rationale || ''))}
            ${this.kv('Next action', this.escapeHtml(r.nextAction || ''))}
            ${this.kv('Decision reason', this.escapeHtml(r.decisionReason || ''))}
            ${this.renderSteps('Steps', r.steps)}
            ${this.renderList('Required info', r.requiredInfo)}
            ${this.renderList('Assumptions', r.assumptions)}
        `);
    }

    renderPlanning(p) {
        return this.section('📋 Planning ()', '1.4', `
            ${this.kv('Detected intent', this.pill(p.detectedIntent || '—', 'pill'))}
            ${this.kv('Intent confidence', this.confidenceBar(p.intentConfidence))}
            ${this.kv('Selected agent', `<code>${this.escapeHtml(p.selectedAgent || '—')}</code>`)}
            ${this.kv('Rationale', this.escapeHtml(p.rationale || ''))}
            ${this.kv('Needs clarification', this.pill(p.needsClarification ? 'yes' : 'no', p.needsClarification ? 'pill pill-yellow' : 'pill'))}
            ${p.clarificationQuestion ? this.kv('Clarification question', `<em>${this.escapeHtml(p.clarificationQuestion)}</em>`) : ''}
            ${this.renderList('Supporting evidence', p.supportingEvidence)}
            ${this.renderAlternatives(p.alternativesRejected)}
            ${p.taskDescription ? this.kv('Task description', this.escapeHtml(p.taskDescription)) : ''}
        `);
    }

    renderAlternatives(alts) {
        if (!alts?.length) return '';
        const rows = alts.map(a =>
            `<li><code>${this.escapeHtml(a.agent || '—')}</code>: ${this.escapeHtml(a.reason || '')}</li>`
        ).join('');
        return this.kv('Rejected alternatives', `<ul style="margin:0;padding-left:18px">${rows}</ul>`);
    }

    renderSpecialists(list) {
        const cards = list.map(s => {
            const pre = s.preToolCheck;
            const refl = s.postToolReflection;
            return `
                <div class="specialist-card">
                    <h4>🛠 ${this.escapeHtml(s.agentName || '—')}</h4>
                    ${pre ? `
                        <div class="trace-section-tag">PRE-TOOL CHECK ()</div>
                        ${this.kv('Can proceed', this.pill(pre.canProceed ? 'yes' : 'no', pre.canProceed ? 'pill pill-green' : 'pill pill-red'))}
                        ${this.kv('Required', this.pillList(pre.requiredParams))}
                        ${this.kv('Collected', this.pillList(pre.collectedParams, 'pill-green'))}
                        ${this.kv('Missing', this.pillList(pre.missingParams, 'pill-red'))}
                        ${pre.reasoning ? this.kv('Reasoning', this.escapeHtml(pre.reasoning)) : ''}
                        ${this.kv('Confidence', this.confidenceBar(pre.confidence))}
                    ` : ''}
                    ${s.resultConfidence != null ? `
                        <div class="trace-section-tag" style="margin-top:10px">RESULT ()</div>
                        ${this.kv('Confidence', this.confidenceBar(s.resultConfidence))}
                        ${s.resultNotes ? this.kv('Notes', this.escapeHtml(s.resultNotes)) : ''}
                    ` : ''}
                    ${refl ? `
                        <div class="trace-section-tag" style="margin-top:10px">POST-TOOL REFLECTION ()</div>
                        ${this.kv('Status', this.pill(refl.status || '—', this.statusPillClass(refl.status)))}
                        ${this.kv('Task complete', this.pill(refl.taskComplete ? 'yes' : 'no', refl.taskComplete ? 'pill pill-green' : 'pill pill-red'))}
                        ${refl.handoffSuggestion ? this.kv('Handoff', `<code>${this.escapeHtml(refl.handoffSuggestion)}</code>`) : ''}
                        ${refl.handoffReason ? this.kv('Handoff reason', this.escapeHtml(refl.handoffReason)) : ''}
                        ${refl.summary ? this.kv('Summary', this.escapeHtml(refl.summary)) : ''}
                        ${this.renderList('Missing context', refl.missingContext)}
                    ` : ''}
                </div>
            `;
        }).join('');

        return this.section('🔧 Specialist Reasonings', '3', cards);
    }

    renderAgentVisits(visits) {
        const rows = visits.map(v => `
            <div class="agent-visit">
                <code>${this.escapeHtml(this.simplifyAgentName(v.agentName))}</code>
                <span style="margin-left:auto">${v.durationMs != null ? v.durationMs + 'ms' : ''}</span>
            </div>
        `).join('');
        return this.section('👥 Agent Visits', '', rows);
    }

    renderFinalCritique(c) {
        const toneClass = c.tone === 'appropriate' ? 'pill-green' : 'pill-yellow';
        const halluClass = c.hallucinationRisk > 0.3 ? 'pill-red' : 'pill-green';
        return this.section('🎯 Final Critique', '', `
            ${this.kv('Addresses query', this.pill(c.addressesUserQuery ? 'yes' : 'no', c.addressesUserQuery ? 'pill pill-green' : 'pill pill-red'))}
            ${this.kv('Tone', this.pill(c.tone || '—', `pill ${toneClass}`))}
            ${this.kv('Completeness', this.confidenceBar(c.completeness))}
            ${this.kv('Hallucination risk', this.pill(c.hallucinationRisk?.toFixed(2) ?? '0.00', `pill ${halluClass}`))}
            ${this.kv('Revision needed', this.pill(c.revisionNeeded ? 'yes' : 'no', c.revisionNeeded ? 'pill pill-yellow' : 'pill pill-green'))}
            ${this.renderList('Sources', c.sources)}
            ${this.renderList('Issues found', c.issuesFound)}
            ${c.revisionNotes ? this.kv('Revision notes', this.escapeHtml(c.revisionNotes)) : ''}
        `);
    }

    renderRevision(t) {
        return this.section('♻️ Revizyon (ileri)', 'REVISE', `
            ${this.kv('İlk taslak', `<div class="response-box draft">${this.escapeHtml(t.firstDraftResponse || '')}</div>`)}
            ${this.kv('Revize edilmiş', `<div class="response-box revised">${this.escapeHtml(t.finalResponse || '')}</div>`)}
        `);
    }

    renderFinalResponse(t) {
        if (t.wasRevised) return ''; // Revision section already shows it
        return this.section('💬 Final Response', '', `
            <div class="response-box">${this.escapeHtml(t.finalResponse || '')}</div>
        `);
    }

    // ─── Helpers ───

    section(title, tag, body) {
        return `
            <div class="trace-section">
                <div class="trace-section-header">
                    <svg class="chev" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="m6 9 6 6 6-6"/>
                    </svg>
                    <span>${title}</span>
                    ${tag ? `<span class="trace-section-tag" style="margin-left:auto">${this.escapeHtml(tag)}</span>` : ''}
                </div>
                <div class="trace-section-body">${body}</div>
            </div>
        `;
    }

    kv(key, val) {
        return `<div class="kv-row"><div class="kv-key">${this.escapeHtml(key)}</div><div class="kv-val">${val}</div></div>`;
    }

    renderList(label, items) {
        if (!items?.length) return '';
        return this.kv(label, this.pillList(items));
    }

    pillList(items, extraClass = '') {
        if (!items?.length) return '<span class="muted">—</span>';
        return `<div class="pill-list">${items.map(i => `<span class="pill ${extraClass}">${this.escapeHtml(this.getItemText(i))}</span>`).join('')}</div>`;
    }

    // Reasoning step'leri {description, action, grounding, confidence} object'i.
    // Legacy string'ler de desteklenir.
    renderSteps(label, items) {
        if (!items?.length) return '';
        const pills = items.map(s => this.renderStepPill(s)).join('');
        return this.kv(label, `<div class="pill-list">${pills}</div>`);
    }

    renderStepPill(step) {
        const text = this.escapeHtml(this.getItemText(step));
        if (!step || typeof step === 'string') {
            return `<span class="pill">${text}</span>`;
        }
        const chips = [];
        if (step.grounding) {
            const cls = step.grounding === 'assumption' ? 'step-chip warn' : 'step-chip';
            chips.push(`<span class="${cls}">${this.escapeHtml(step.grounding)}</span>`);
        }
        if (typeof step.confidence === 'number') {
            chips.push(`<span class="step-chip conf">${Math.round(step.confidence * 100)}%</span>`);
        }
        if (step.action) {
            chips.push(`<span class="step-chip action">${this.escapeHtml(step.action)}</span>`);
        }
        const chipsHtml = chips.length ? `<span class="step-chips">${chips.join('')}</span>` : '';
        return `<span class="pill step-pill"><span class="step-text">${text}</span>${chipsHtml}</span>`;
    }

    getItemText(item) {
        if (item == null) return '';
        if (typeof item === 'string') return item;
        if (typeof item === 'object') {
            return item.description || item.step || item.label || item.text || JSON.stringify(item);
        }
        return String(item);
    }

    pill(text, className = 'pill') {
        return `<span class="${className}">${this.escapeHtml(text)}</span>`;
    }

    confidenceBar(score) {
        if (score == null || isNaN(score)) return '<span class="muted">—</span>';
        const pct = Math.round(score * 100);
        return `
            <span class="confidence-bar">
                <span class="confidence-bar-track">
                    <span class="confidence-bar-fill" style="width:${pct}%"></span>
                </span>
                <span>${pct}%</span>
            </span>
        `;
    }

    statusPillClass(status) {
        switch (status) {
            case 'done': return 'pill pill-green';
            case 'failed':
            case 'needs_escalation': return 'pill pill-red';
            case 'needs_followup':
            case 'partial': return 'pill pill-yellow';
            default: return 'pill';
        }
    }

    scoreFromString(s) {
        if (typeof s === 'number') return s;
        const map = { yüksek: 0.9, orta: 0.6, düşük: 0.3, bilinmiyor: 0.2 };
        return map[(s || '').toLowerCase()] ?? null;
    }

    simplifyAgentName(name) {
        if (!name) return '—';
        return name.split('_')[0];
    }

    formatTime(iso) {
        try {
            const d = new Date(iso);
            const now = new Date();
            const diffMs = now - d;
            if (diffMs < 60000) return `${Math.floor(diffMs / 1000)}s önce`;
            if (diffMs < 3600000) return `${Math.floor(diffMs / 60000)}dk önce`;
            if (diffMs < 86400000) return `${Math.floor(diffMs / 3600000)}sa önce`;
            return d.toLocaleDateString('tr-TR');
        } catch {
            return '—';
        }
    }

    escapeHtml(str) {
        if (str == null) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}

// ─── UYGULAMA BAŞLATMA ───
document.addEventListener('DOMContentLoaded', () => {
    const dashboard = new TraceDashboard();
    dashboard.init();
});
