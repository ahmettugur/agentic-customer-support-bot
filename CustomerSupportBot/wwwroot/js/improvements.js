// js/improvements.js — Self-improving loop admin tab
// Endpoints:
//   POST /improvements/mine
//   GET  /improvements?status=Proposed|Approved|Rejected
//   POST /improvements/{id}/approve  (body: { reason? })
//   POST /improvements/{id}/reject   (body: { reason? })

(function () {
    const API = window.location.origin;

    const els = {
        mineBtn: document.getElementById('mineLessonsBtn'),
        stats: document.getElementById('improvementsStats'),
        proposedList: document.getElementById('proposedLessonsList'),
        approvedList: document.getElementById('approvedLessonsList'),
        proposedBadge: document.getElementById('proposedLessonsBadge'),
        tabBtn: document.querySelector('.tab-btn[data-tab="improvements"]')
    };
    if (!els.mineBtn) return; // sayfa farklıysa atla

    async function fetchJson(url, options = {}) {
        const res = await window.Auth.fetch(`${API}${url}`, options);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return res.json();
    }

    function escape(s) {
        if (s == null) return '';
        return String(s).replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
    }

    function lessonCard(l, isProposed) {
        const div = document.createElement('div');
        div.className = 'card';
        const decisionInfo = l.decidedBy
            ? `<div class="muted small" style="margin-top:6px">${escape(l.status)} · ${escape(l.decidedBy)} · ${new Date(l.decidedAt).toLocaleString('tr-TR')}${l.decisionReason ? ' · ' + escape(l.decisionReason) : ''}</div>`
            : '';
        div.innerHTML = `
            <header class="card-header">
                <div class="card-title">
                    <strong>${escape(l.title || '(başlıksız)')}</strong>
                    ${l.suggestedAgent ? `<span class="badge">${escape(l.suggestedAgent)}</span>` : ''}
                </div>
                <code class="card-id">${escape(l.id?.substring(0, 8))}…</code>
            </header>
            <div class="card-body">
                <p style="margin:8px 0">${escape(l.lessonText)}</p>
                ${l.observation ? `<p class="muted small"><em>Gözlem:</em> ${escape(l.observation)}</p>` : ''}
                ${l.sourceTraceIds?.length ? `<p class="muted small">Kaynak trace'ler: ${l.sourceTraceIds.slice(0, 3).map(t => `<a href="/replay.html?traceId=${encodeURIComponent(t)}" target="_blank"><code>${escape(t.substring(0, 8))}</code></a>`).join(', ')}${l.sourceTraceIds.length > 3 ? ` (+${l.sourceTraceIds.length - 3})` : ''}</p>` : ''}
                ${decisionInfo}
            </div>
            ${isProposed ? `
            <footer class="card-footer">
                <button class="btn-primary" data-act="approve" data-id="${l.id}">✅ Onayla</button>
                <button class="btn-danger"  data-act="reject"  data-id="${l.id}">❌ Reddet</button>
            </footer>` : ''}`;
        if (isProposed) {
            div.querySelectorAll('button[data-act]').forEach(b => {
                b.addEventListener('click', () => decide(b.dataset.id, b.dataset.act));
            });
        }
        return div;
    }

    async function decide(id, act) {
        const reason = prompt(act === 'approve' ? 'Onay gerekçesi (ops.):' : 'Red gerekçesi (ops.):');
        try {
            await fetchJson(`/improvements/${encodeURIComponent(id)}/${act}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reason })
            });
            await reload();
        } catch (e) {
            alert(`İşlem başarısız: ${e.message}`);
        }
    }

    async function mine() {
        els.mineBtn.disabled = true;
        els.mineBtn.textContent = '⏳ Taranıyor…';
        try {
            const r = await fetchJson('/improvements/mine', { method: 'POST' });
            els.stats.textContent = `Son tarama: ${r.candidates ?? 0} aday → ${r.proposedLessons ?? 0} yeni öneri${r.error ? ' · HATA: ' + r.error : ''}`;
            await reload();
        } catch (e) {
            els.stats.textContent = `Hata: ${e.message}`;
        } finally {
            els.mineBtn.disabled = false;
            els.mineBtn.textContent = '⚙️ Yeni Tarama Çalıştır';
        }
    }

    async function reload() {
        try {
            const [proposed, approved] = await Promise.all([
                fetchJson('/improvements?status=Proposed'),
                fetchJson('/improvements?status=Approved')
            ]);
            renderList(els.proposedList, proposed, true);
            renderList(els.approvedList, approved, false);

            if (els.proposedBadge) els.proposedBadge.textContent = String(proposed.length);
        } catch (e) {
            els.proposedList.innerHTML = `<div class="empty-state">Yüklenemedi: ${escape(e.message)}</div>`;
        }
    }

    function renderList(container, list, isProposed) {
        container.innerHTML = '';
        if (!list || list.length === 0) {
            container.innerHTML = '<div class="empty-state">Kayıt yok.</div>';
            return;
        }
        list.forEach(l => container.appendChild(lessonCard(l, isProposed)));
    }

    els.mineBtn.addEventListener('click', mine);
    if (els.tabBtn) els.tabBtn.addEventListener('click', reload);

    // İlk yükleme
    reload();
})();
