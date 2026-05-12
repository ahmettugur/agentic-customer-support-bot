// js/sla.js
// SLA Guardian dashboard. Auth: window.Auth (js/auth.js).

(function () {
    if (!window.Auth) {
        console.error('[sla] window.Auth yok. js/auth.js yüklenmemiş.');
        return;
    }
    if (!window.Auth.requireAuth()) return;

    const $ = id => document.getElementById(id);
    let timer = null;

    function fmtTs(iso) {
        if (!iso) return '—';
        try {
            const d = new Date(iso);
            return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
        } catch { return iso; }
    }

    function statValueClass(actual, warn, breach) {
        if (actual >= breach) return 'breach';
        if (actual >= warn) return 'warn';
        return '';
    }

    function row(label, value, cls) {
        return `<div class="sla-stat-row"><span class="label">${label}</span><span class="value ${cls || ''}">${value}</span></div>`;
    }

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, ch => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        })[ch]);
    }

    async function loadStatus() {
        try {
            const [s, e] = await Promise.all([
                window.Auth.fetch('/sla/status'),
                window.Auth.fetch('/sla/events?count=100')
            ]);
            if (!s.ok) throw new Error('status ' + s.status);
            if (!e.ok) throw new Error('events ' + e.status);

            const status = await s.json();
            const events = await e.json();

            renderStatus(status);
            renderEvents(events.items || []);
        } catch (err) {
            console.error('SLA load failed', err);
        }
    }

    function renderStatus(s) {
        const pill = $('enabledPill');
        if (s.enabled) {
            pill.textContent = `aktif · ${s.pollIntervalSeconds}sn`;
            pill.className = 'pill ok';
        } else {
            pill.textContent = 'pasif';
            pill.className = 'pill off';
        }

        const a = s.approvals || {};
        $('approvalsBlock').innerHTML =
            row('Bekleyen sayı', a.pendingCount ?? 0) +
            row('En eski (sn)', a.oldestSeconds ?? 0,
                statValueClass(a.oldestSeconds ?? 0, a.warnAfter ?? 0, a.breachAfter ?? 0)) +
            row('Warn eşiği (sn)', a.warnAfter ?? 0) +
            row('Breach eşiği (sn)', a.breachAfter ?? 0) +
            row('Breach aksiyonu', a.onBreach ?? '—') +
            row('Son ihlaller', a.breachCountRecent ?? 0,
                (a.breachCountRecent ?? 0) > 0 ? 'breach' : '');

        const ev = s.escalations || {};
        $('escalationsBlock').innerHTML =
            row('Açık sayı', ev.openCount ?? 0) +
            row('En eski (sn)', ev.oldestSeconds ?? 0,
                statValueClass(ev.oldestSeconds ?? 0, ev.warnAfter ?? 0, ev.breachAfter ?? 0)) +
            row('Warn eşiği (sn)', ev.warnAfter ?? 0) +
            row('Breach eşiği (sn)', ev.breachAfter ?? 0) +
            row('Priority boost', ev.boostPriorityOnBreach ? 'aktif' : 'pasif') +
            row('Son ihlaller', ev.breachCountRecent ?? 0,
                (ev.breachCountRecent ?? 0) > 0 ? 'breach' : '');
    }

    function renderEvents(items) {
        const el = $('eventList');
        if (!items.length) {
            el.innerHTML = '<div class="sla-empty">Henüz olay yok.</div>';
            return;
        }
        el.innerHTML = items.map(it => {
            const sev = (it.severity || '').toLowerCase();
            return `
        <div class="sla-event">
          <span class="ts" title="${escapeHtml(it.timestamp)}">${escapeHtml(fmtTs(it.timestamp))}</span>
          <span class="kind">${escapeHtml(it.kind)}</span>
          <span class="severity ${sev}">${escapeHtml(it.severity)}</span>
          <span class="target">${escapeHtml(it.targetId)}${it.action ? ' · ' + escapeHtml(it.action) : ''}</span>
          <span class="age">${it.ageSeconds ?? 0}s</span>
          ${it.note ? `<span class="note">${escapeHtml(it.note)}</span>` : ''}
        </div>
      `;
        }).join('');
    }

    function startTimer() {
        if (timer) clearInterval(timer);
        timer = setInterval(loadStatus, 5000);
    }

    function bindUi() {
        $('refreshBtn').addEventListener('click', loadStatus);
        $('autoRefresh').addEventListener('change', e => {
            if (e.target.checked) startTimer();
            else if (timer) { clearInterval(timer); timer = null; }
        });
    }

    function init() {
        bindUi();
        loadStatus();
        startTimer();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
