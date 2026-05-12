// js/replay.js — Trace step-by-step replay player
// Uses GET /traces/{id} (admin-protected) and constructs an ordered timeline:
//   1. Init (user query)
//   2. Reasoning (if exists)
//   3. Planning (if exists)
//   4. AgentVisits (chronological)
//   5. SpecialistReasonings (interleaved by agent)
//   6. ToolCalls (chronological)
//   7. Final response

class TraceReplay {
    static API_BASE = window.location.origin;

    state = {
        traceId: null,
        trace: null,
        steps: [],
        idx: 0,
        playing: false,
        timer: null,
        speedMs: 1000
    };

    els = {};

    constructor() {
        this.els = {
            input: document.getElementById('traceIdInput'),
            loadBtn: document.getElementById('loadTraceBtn'),
            meta: document.getElementById('replayMeta'),
            controls: document.getElementById('replayControls'),
            main: document.getElementById('replayMain'),
            empty: document.getElementById('replayEmpty'),
            timeline: document.getElementById('timelineList'),
            stepLabel: document.getElementById('stepLabel'),
            detailNum: document.getElementById('detailStepNum'),
            detailKind: document.getElementById('detailStepKind'),
            detailTime: document.getElementById('detailStepTime'),
            detailBody: document.getElementById('detailBody'),
            btnFirst: document.getElementById('btnFirst'),
            btnPrev: document.getElementById('btnPrev'),
            btnPlay: document.getElementById('btnPlay'),
            btnNext: document.getElementById('btnNext'),
            btnLast: document.getElementById('btnLast'),
            speed: document.getElementById('speedSelect')
        };
    }

    init() {
        this.els.loadBtn.addEventListener('click', () => this.loadFromInput());
        this.els.input.addEventListener('keydown', e => { if (e.key === 'Enter') this.loadFromInput(); });

        this.els.btnFirst.addEventListener('click', () => this.go(0));
        this.els.btnPrev.addEventListener('click', () => this.go(this.state.idx - 1));
        this.els.btnNext.addEventListener('click', () => this.go(this.state.idx + 1));
        this.els.btnLast.addEventListener('click', () => this.go(this.state.steps.length - 1));
        this.els.btnPlay.addEventListener('click', () => this.togglePlay());
        this.els.speed.addEventListener('change', () => {
            this.state.speedMs = parseInt(this.els.speed.value, 10);
            if (this.state.playing) { this.stopPlay(); this.startPlay(); }
        });

        // ?traceId=xxx parametresinden otomatik yükle
        const url = new URL(window.location);
        const tid = url.searchParams.get('traceId');
        if (tid) {
            this.els.input.value = tid;
            this.load(tid);
        }
    }

    loadFromInput() {
        const id = (this.els.input.value || '').trim();
        if (!id) return;
        this.load(id);
    }

    async load(traceId) {
        try {
            const res = await window.Auth.fetch(`${TraceReplay.API_BASE}/traces/${encodeURIComponent(traceId)}`);
            if (!res.ok) {
                this.els.meta.textContent = `Trace bulunamadı: ${traceId} (HTTP ${res.status})`;
                return;
            }
            const trace = await res.json();
            this.state.traceId = traceId;
            this.state.trace = trace;
            this.state.steps = this.buildSteps(trace);
            this.state.idx = 0;
            this.renderTimeline();
            this.go(0);

            this.els.meta.textContent = `${trace.userQuery?.slice(0, 70) || '(soru yok)'}${(trace.userQuery?.length || 0) > 70 ? '…' : ''}`;
            this.els.controls.hidden = false;
            this.els.main.hidden = false;
            this.els.empty.hidden = true;

            // URL'i güncelle (deep link)
            const url = new URL(window.location);
            url.searchParams.set('traceId', traceId);
            window.history.replaceState({}, '', url);
        } catch (err) {
            this.els.meta.textContent = `Hata: ${err.message}`;
        }
    }

    buildSteps(trace) {
        const steps = [];

        steps.push({
            kind: 'init',
            time: trace.startedAt,
            title: 'Kullanıcı Sorusu',
            payload: { sessionId: trace.sessionId, userQuery: trace.userQuery, traceId: trace.traceId }
        });

        if (trace.reasoning) {
            steps.push({
                kind: 'reasoning',
                time: trace.startedAt,
                title: 'Pre-analysis Reasoning',
                payload: trace.reasoning
            });
        }
        if (trace.planning) {
            steps.push({
                kind: 'planning',
                time: trace.startedAt,
                title: `Planning → ${trace.planning.selectedAgent || trace.planning.detectedIntent || '?'}`,
                payload: trace.planning
            });
        }

        // AgentVisits + interleave SpecialistReasonings + ToolCalls by time
        const events = [];
        for (const v of (trace.agentVisits || [])) {
            events.push({ t: v.startedAt, kind: 'agent', title: `Agent: ${v.agentName}`, payload: v });
        }
        for (const sr of (trace.specialistReasonings || [])) {
            events.push({
                t: sr.recordedAt || trace.startedAt,
                kind: 'reasoning',
                title: `Specialist Reasoning: ${sr.agentName || '?'}`,
                payload: sr
            });
        }
        for (const tc of (trace.toolCalls || [])) {
            events.push({ t: tc.invokedAt, kind: 'tool', title: `Tool: ${tc.toolName}`, payload: tc });
        }
        events.sort((a, b) => new Date(a.t).getTime() - new Date(b.t).getTime());
        for (const e of events) steps.push({ kind: e.kind, time: e.t, title: e.title, payload: e.payload });

        steps.push({
            kind: 'final',
            time: trace.completedAt || trace.startedAt,
            title: `Final Yanıt (${trace.terminationReason || '?'})`,
            payload: {
                terminationReason: trace.terminationReason,
                durationMs: trace.durationMs,
                iterationCount: trace.iterationCount,
                error: trace.error,
                response: trace.finalResponse
            }
        });
        return steps;
    }

    renderTimeline() {
        this.els.timeline.innerHTML = '';
        this.state.steps.forEach((step, i) => {
            const li = document.createElement('li');
            li.dataset.idx = i;
            li.innerHTML = `
                <span class="step-num">#${i + 1}</span>
                <div class="step-content">
                    <div class="step-title">
                        <span class="step-kind-pill kind-${step.kind}">${step.kind}</span>
                        ${this.escape(step.title)}
                    </div>
                    <div class="step-meta">${this.formatTime(step.time)}</div>
                </div>`;
            li.addEventListener('click', () => this.go(i));
            this.els.timeline.appendChild(li);
        });
    }

    go(i) {
        if (i < 0) i = 0;
        if (i >= this.state.steps.length) {
            i = this.state.steps.length - 1;
            this.stopPlay();
        }
        this.state.idx = i;
        const step = this.state.steps[i];

        // timeline highlight
        const items = this.els.timeline.querySelectorAll('li');
        items.forEach((li, j) => {
            li.classList.toggle('active', j === i);
            li.classList.toggle('passed', j < i);
        });
        items[i]?.scrollIntoView({ block: 'nearest' });

        // detail
        this.els.detailNum.textContent = `#${i + 1}`;
        this.els.detailKind.textContent = step.kind;
        this.els.detailTime.textContent = this.formatTime(step.time);
        this.els.detailBody.innerHTML = this.renderPayload(step);
        this.els.stepLabel.textContent = `${i + 1} / ${this.state.steps.length}`;
    }

    renderPayload(step) {
        const p = step.payload || {};

        if (step.kind === 'init') {
            return `
                <div class="field-row"><div class="field-key">Trace ID</div><div class="field-val">${this.escape(p.traceId)}</div></div>
                <div class="field-row"><div class="field-key">Session</div><div class="field-val">${this.escape(p.sessionId)}</div></div>
                <h4>Soru</h4>
                <pre>${this.escape(p.userQuery || '')}</pre>`;
        }
        if (step.kind === 'final') {
            return `
                <div class="field-row"><div class="field-key">Termination</div><div class="field-val">${this.escape(p.terminationReason || '?')}</div></div>
                <div class="field-row"><div class="field-key">Iterasyon</div><div class="field-val">${p.iterationCount ?? '?'}</div></div>
                <div class="field-row"><div class="field-key">Süre</div><div class="field-val">${p.durationMs != null ? p.durationMs + ' ms' : '?'}</div></div>
                ${p.error ? `<div class="field-row"><div class="field-key">Hata</div><div class="field-val" style="color:#c00">${this.escape(p.error)}</div></div>` : ''}
                <h4>Yanıt</h4>
                <pre>${this.escape(p.response || '(boş)')}</pre>`;
        }
        if (step.kind === 'tool') {
            return `
                <div class="field-row"><div class="field-key">Tool</div><div class="field-val">${this.escape(p.toolName)}</div></div>
                <div class="field-row"><div class="field-key">Agent</div><div class="field-val">${this.escape(p.agentName || '?')}</div></div>
                <div class="field-row"><div class="field-key">Success</div><div class="field-val">${p.success ? '✅' : '❌'}</div></div>
                <h4>Parametreler</h4><pre>${this.escape(p.parametersSummary || '(yok)')}</pre>
                <h4>Sonuç</h4><pre>${this.escape(p.resultSummary || '(yok)')}</pre>`;
        }
        if (step.kind === 'agent') {
            return `
                <div class="field-row"><div class="field-key">Agent</div><div class="field-val">${this.escape(p.agentName)}</div></div>
                <div class="field-row"><div class="field-key">Süre</div><div class="field-val">${p.durationMs != null ? p.durationMs + ' ms' : 'devam ediyor'}</div></div>
                ${p.output ? `<h4>Çıktı</h4><pre>${this.escape(p.output)}</pre>` : '<p class="muted">Çıktı kaydedilmemiş.</p>'}`;
        }
        // reasoning, planning, vb. — generic JSON dump
        return `<pre>${this.escape(JSON.stringify(p, null, 2))}</pre>`;
    }

    togglePlay() {
        if (this.state.playing) this.stopPlay();
        else this.startPlay();
    }

    startPlay() {
        this.state.playing = true;
        this.els.btnPlay.textContent = '⏸ Duraklat';
        this.state.timer = setInterval(() => {
            if (this.state.idx >= this.state.steps.length - 1) { this.stopPlay(); return; }
            this.go(this.state.idx + 1);
        }, this.state.speedMs);
    }

    stopPlay() {
        this.state.playing = false;
        this.els.btnPlay.textContent = '▶ Oynat';
        if (this.state.timer) { clearInterval(this.state.timer); this.state.timer = null; }
    }

    formatTime(t) {
        if (!t) return '';
        try {
            const d = new Date(t);
            return d.toLocaleTimeString('tr-TR', { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
        } catch { return String(t); }
    }

    escape(s) {
        if (s == null) return '';
        return String(s).replace(/[&<>"']/g, m => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[m]));
    }
}

const replay = new TraceReplay();
replay.init();
