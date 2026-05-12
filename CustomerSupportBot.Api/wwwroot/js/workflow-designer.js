// js/workflow-designer.js
// Workflow Designer sayfasının davranışı.
// Auth: window.Auth (js/auth.js) üzerinden — Bearer token otomatik eklenir.

(function () {
    if (!window.Auth) {
        console.error('[workflow-designer] window.Auth yok. js/auth.js yüklenmemiş.');
        return;
    }
    if (!window.Auth.requireAuth()) {
        // requireAuth zaten login.html'e yönlendirir
        return;
    }

    const api = (url, opts = {}) => window.Auth.fetch(url, {
        ...opts,
        headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) }
    });

    let currentId = null;
    let currentList = [];

    const $ = id => document.getElementById(id);

    const SAMPLE = {
        name: "Sipariş Durumu Hızlı Yanıt",
        description: "ORD- ile başlayan sipariş numarası varsa OrderStatus tool'unu çağırır.",
        isActive: true,
        triggerKeywords: ["sipariş", "durumu", "kargo"],
        inputPatterns: { "orderId": "(ORD-\\d+)" },
        steps: [
            { type: "Branch", label: "Sipariş ID var mı?", condition: "orderId exists", skipNext: 2 },
            { type: "Respond", template: "Sipariş numaranızı paylaşır mısınız (ör. ORD-1)?" },
            { type: "Branch", condition: "true == true", skipNext: 99 },
            { type: "Lookup", tool: "order_status_tool", parameters: { "orderId": "$orderId" }, storeAs: "lookup" },
            { type: "Respond", template: "📦 {lookup}" }
        ]
    };

    async function loadList() {
        const r = await api('/workflows');
        if (!r.ok) {
            alert('Liste alınamadı: ' + r.status + (r.status === 403 ? ' (Admin yetkisi gerekiyor)' : ''));
            return;
        }
        const j = await r.json();
        currentList = j.items || [];
        renderList();
    }

    function renderList() {
        $('list').innerHTML = currentList.map(d => `
      <div class="list-item ${d.id === currentId ? 'active' : ''}" data-id="${d.id}">
        <strong>${d.name}</strong>
        <span class="badge ${d.isActive ? 'active' : 'inactive'}">${d.isActive ? 'aktif' : 'pasif'}</span>
        <div class="muted">${d.id} · v${d.version} · ${d.steps.length} adım</div>
      </div>`).join('') || '<p class="muted">Henüz workflow yok.</p>';
        document.querySelectorAll('.list-item').forEach(el => {
            el.addEventListener('click', () => loadDef(el.dataset.id));
        });
    }

    async function loadDef(id) {
        const r = await api('/workflows/' + encodeURIComponent(id));
        if (!r.ok) return;
        const def = await r.json();
        currentId = def.id;
        $('editor').value = JSON.stringify(def, null, 2);
        $('meta').textContent = `Last update: ${def.updatedAt} (v${def.version}) · by ${def.updatedBy ?? '—'}`;
        renderList();
    }

    function bindUi() {
        $('newBtn').onclick = () => {
            currentId = null;
            $('editor').value = JSON.stringify(SAMPLE, null, 2);
            $('meta').textContent = '';
            renderList();
        };
        $('reloadBtn').onclick = loadList;

        $('saveBtn').onclick = async () => {
            let body;
            try { body = JSON.parse($('editor').value); }
            catch (e) { alert('JSON parse hatası: ' + e.message); return; }
            const url = currentId ? '/workflows/' + encodeURIComponent(currentId) : '/workflows';
            const method = currentId ? 'PUT' : 'POST';
            const r = await api(url, { method, body: JSON.stringify(body) });
            if (!r.ok) { alert('Kaydetme başarısız: ' + r.status); return; }
            const saved = await r.json();
            currentId = saved.id;
            await loadList();
            await loadDef(currentId);
        };

        $('deleteBtn').onclick = async () => {
            if (!currentId) { alert('Önce yüklü bir workflow seçin.'); return; }
            if (!confirm('Silinsin mi?')) return;
            const r = await api('/workflows/' + encodeURIComponent(currentId), { method: 'DELETE' });
            if (r.ok) {
                currentId = null;
                $('editor').value = '';
                await loadList();
            }
        };

        $('testBtn').onclick = async () => {
            if (!currentId) { alert('Önce kaydedilmiş bir workflow seçin.'); return; }
            const input = $('testInput').value;
            const r = await api('/workflows/' + encodeURIComponent(currentId) + '/test', {
                method: 'POST', body: JSON.stringify({ input })
            });
            const j = await r.json();
            $('testResult').textContent = JSON.stringify(j, null, 2);
        };
    }

    function init() {
        bindUi();
        loadList();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
