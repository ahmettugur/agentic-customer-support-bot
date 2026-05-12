// Js/admin.js
// HITL Admin Panel — class tabanlı vanilla JS.
// Polling bazlı auto-refresh (3sn) + tab navigasyonu + approve/reject/resolve aksiyonları.

class AdminPanel {
    // Admin endpoint'leri
    static ADMIN_API = {
        pendingApprovals: '/approvals/pending',
        recentApprovals: '/approvals/recent?count=50',
        approve: id => `/approvals/${id}/approve`,
        reject: id => `/approvals/${id}/reject`,
        openEscalations: '/escalations/open',
        recentEscalations: '/escalations/recent?count=50',
        acknowledge: id => `/escalations/${id}/acknowledge`,
        resolve: id => `/escalations/${id}/resolve`,
        dismiss: id => `/escalations/${id}/dismiss`,
        replanEscalation: id => `/escalations/${id}/replan`,
        replanChat: sid => `/chat-sessions/${sid}/replan`,
        // HITL Live Takeover
        activeChats: '/chat-sessions/active',
        // Analytics
        analyticsDashboard: '/analytics/dashboard',
        sessionAnalytics: sid => `/analytics/session/${sid}`,
        sessionsList: '/sessions/',
        // Sentiment
        sessionSentiment: sid => `/chat-sessions/${sid}/sentiment`,
        chatHistory: sid => `/chat-sessions/${sid}/history?take=200`,
        takeover: sid => `/chat-sessions/${sid}/takeover`,
        release: sid => `/chat-sessions/${sid}/release`,
        sendChat: sid => `/chat-sessions/${sid}/messages`,
        subscribeChat: sid => `/chat-sessions/${sid}/subscribe`
    };

    // Agent endpoint'leri — /agent/* prefix kullanır
    static AGENT_API = {
        pendingApprovals: '/agent/approvals/pending',
        recentApprovals: '/agent/approvals/pending',
        approve: id => `/agent/approvals/${id}/approve`,
        reject: id => `/agent/approvals/${id}/reject`,
        openEscalations: '/agent/escalations/open',
        myEscalations: '/agent/escalations/my',
        recentEscalations: '/agent/escalations/open',
        acknowledge: id => `/agent/escalations/${id}/acknowledge`,
        resolve: id => `/agent/escalations/${id}/resolve`,
        dismiss: id => `/agent/escalations/${id}/dismiss`,
        replanEscalation: id => `/agent/escalations/${id}/replan`,
        replanChat: sid => `/agent/chat-sessions/${sid}/replan`,
        // HITL Live Takeover
        activeChats: '/agent/chat-sessions/active',
        // Analytics — agent erişemez, boş döner
        analyticsDashboard: null,
        sessionAnalytics: sid => null,
        sessionsList: null,
        // Sentiment
        sessionSentiment: sid => `/agent/chat-sessions/${sid}/sentiment`,
        chatHistory: sid => `/agent/chat-sessions/${sid}/history?take=200`,
        takeover: sid => `/agent/chat-sessions/${sid}/takeover`,
        release: sid => `/agent/chat-sessions/${sid}/release`,
        sendChat: sid => `/agent/chat-sessions/${sid}/messages`,
        subscribeChat: sid => `/agent/chat-sessions/${sid}/subscribe`
    };

    // Aktif API — role'e göre init()'te belirlenir
    static API = AdminPanel.ADMIN_API;

    static REFRESH_INTERVAL_MS = 3000;

    // Agent listesi cache — "Atama Yap" dropdown için
    _agentList = null;

    // Onaylanırken gerekçe (audit trail) zorunlu olan yan etkili tool'lar.
    // Diğer tool'larda gerekçe opsiyoneldir; admin akışını yavaşlatmamak için.
    static HIGH_RISK_TOOLS = new Set([
        'order_placement_tool',
        'complaint_registration_tool'
    ]);

    refreshTimer = null;

    // Devralınmış (humanAgent atanmış) session ID'leri — eskalasyon kartlarında
    // takeover/ack butonlarını disabled etmek için kullanılır.
    takenOverSessionIds = new Set();

    // Aktif sohbet panel state'i (tek panel açıkta tutuyoruz)
    chatPanelState = {
        sessionId: null,
        humanAgent: null,
        eventSource: null
    };

    // Analytics session state
    analyticsSelectedSessionId = null;

    // Kullanıcı rolü: 'Admin' | 'Agent'
    userRole = 'Admin';
    linkedAgentId = null;

    constructor() {
        this._detectRole();
        this._bindTabs();
        this._bindControls();
        this._bindPromptModal();
    }

    // ─── Role Detection ───
    _detectRole() {
        try {
            const raw = localStorage.getItem('cs.auth');
            if (raw) {
                const auth = JSON.parse(raw);
                this.userRole = auth.role || 'Admin';
                this.linkedAgentId = auth.linkedAgentId || null;
            }
        } catch {}

        // API endpoint'lerini role'e göre seç
        if (this.userRole === 'Agent') {
            AdminPanel.API = AdminPanel.AGENT_API;
        } else {
            AdminPanel.API = AdminPanel.ADMIN_API;
        }
    }

    get isAgent() { return this.userRole === 'Agent'; }

    // ─── Init ───
    init() {
        this._applyRoleUI();
        this.loadAll();
        this.startRefresh();
    }

    // Agent rolü için sadece ilgili tab'ları göster
    _applyRoleUI() {
        if (!this.isAgent) return;

        // Başlığı güncelle
        const title = document.querySelector('.admin-header-info h1');
        if (title) title.textContent = 'Temsilci Paneli';
        const subtitle = document.querySelector('.admin-header-info .muted');
        if (subtitle) subtitle.textContent = 'Atanmış eskalasyonlar & canlı sohbetler';

        // Agent'a gösterilmeyecek tab'lar
        const hiddenTabs = ['approvals', 'history', 'analytics', 'improvements'];
        hiddenTabs.forEach(tab => {
            const btn = document.querySelector(`.tab-btn[data-tab="${tab}"]`);
            if (btn) btn.style.display = 'none';
            const content = document.getElementById(`tab-${tab}`);
            if (content) content.style.display = 'none';
        });

        // Replay ve Traces linklerini gizle
        document.querySelectorAll('.tab-btn--link').forEach(el => el.style.display = 'none');

        // Replan butonlarını agent için gizle (sadece admin kullanır)
        document.querySelectorAll('[data-replan]').forEach(el => el.style.display = 'none');

        // Eskalasyonlar tab'ını aktif yap
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        const escTab = document.querySelector('.tab-btn[data-tab="escalations"]');
        const escContent = document.getElementById('tab-escalations');
        if (escTab) escTab.classList.add('active');
        if (escContent) escContent.classList.add('active');
    }

    // ─── Helpers ───
    $(selector) {
        return document.querySelector(selector);
    }

    $$(selector) {
        return document.querySelectorAll(selector);
    }

    async fetchJson(url, options = {}) {
        const r = await window.Auth.fetch(url, options);
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json();
    }

    formatTime(iso) {
        if (!iso) return '—';
        const d = new Date(iso);
        return d.toLocaleString('tr-TR', {
            hour: '2-digit', minute: '2-digit', second: '2-digit',
            day: '2-digit', month: 'short'
        });
    }

    statusClassName(s) {
        return (s || '').toString().toLowerCase();
    }

    escapeHtml(str) {
        const d = document.createElement('div');
        d.textContent = str ?? '';
        return d.innerHTML;
    }

    // ─── Prompt Modal (native window.prompt yerine modern modal) ───
    // Promise<string|null> döner. null = iptal, string = girilen değer (boş olabilir).
    // options: { title, subtitle, label, placeholder, hint, confirmText, cancelText,
    //           required, variant ('default'|'warning'|'danger'), defaultValue, multiline }
    showPromptModal(options = {}) {
        const overlay = this.$('#promptModal');
        const form = this.$('#promptModalForm');
        const titleEl = this.$('#promptModalTitle');
        const subtitleEl = this.$('#promptModalSubtitle');
        const labelEl = this.$('#promptModalLabel');
        const inputEl = this.$('#promptModalInput');
        const hintEl = this.$('#promptModalHint');
        const errorEl = this.$('#promptModalError');
        const submitBtn = this.$('#promptModalSubmit');
        const cancelBtn = this.$('#promptModalCancel');

        if (!overlay || !form) return Promise.resolve(null);

        // İçeriği doldur
        titleEl.textContent = options.title || 'Bilgi gerekiyor';
        subtitleEl.textContent = options.subtitle || '';
        subtitleEl.style.display = options.subtitle ? '' : 'none';
        labelEl.textContent = options.label || 'Yanıtınız';
        inputEl.placeholder = options.placeholder || '';
        inputEl.value = options.defaultValue || '';
        inputEl.rows = options.multiline === false ? 1 : 3;
        hintEl.textContent = options.hint || '';
        hintEl.style.display = options.hint ? '' : 'none';
        errorEl.classList.add('hidden');
        errorEl.textContent = '';
        submitBtn.textContent = options.confirmText || 'Onayla';
        cancelBtn.textContent = options.cancelText || 'İptal';

        // Variant (renkli header)
        form.classList.remove('variant-warning', 'variant-danger');
        if (options.variant === 'warning') form.classList.add('variant-warning');
        else if (options.variant === 'danger') form.classList.add('variant-danger');

        // Submit butonunun rengini variant'a göre ayarla
        submitBtn.classList.remove('btn-danger', 'btn-primary');
        submitBtn.classList.add(options.variant === 'danger' ? 'btn-danger' : 'btn-primary');

        overlay.classList.remove('hidden');
        // Animasyon olmadan flush odaklan
        setTimeout(() => inputEl.focus(), 0);

        return new Promise(resolve => {
            const cleanup = () => {
                overlay.classList.add('hidden');
                form.removeEventListener('submit', onSubmit);
                cancelBtn.removeEventListener('click', onCancel);
                this.$('#promptModalClose').removeEventListener('click', onCancel);
                overlay.removeEventListener('click', onOverlayClick);
                document.removeEventListener('keydown', onKey);
                this._activePromptResolve = null;
            };
            const onSubmit = (e) => {
                e.preventDefault();
                const value = inputEl.value.trim();
                if (options.required && !value) {
                    errorEl.textContent = options.requiredMessage || 'Bu alan zorunludur.';
                    errorEl.classList.remove('hidden');
                    inputEl.focus();
                    return;
                }
                cleanup();
                resolve(value);
            };
            const onCancel = () => { cleanup(); resolve(null); };
            const onOverlayClick = (e) => { if (e.target === overlay) onCancel(); };
            const onKey = (e) => { if (e.key === 'Escape') onCancel(); };

            this._activePromptResolve = resolve;
            form.addEventListener('submit', onSubmit);
            cancelBtn.addEventListener('click', onCancel);
            this.$('#promptModalClose').addEventListener('click', onCancel);
            overlay.addEventListener('click', onOverlayClick);
            document.addEventListener('keydown', onKey);
        });
    }

    _bindPromptModal() {
        // Modal markup'ı admin.html içinde; binding showPromptModal her açılışta
        // yapılıyor, burada sadece var olduğunu doğruluyoruz.
        if (!document.getElementById('promptModal')) {
            console.warn('promptModal not found in DOM');
        }
    }

    // ─── Select Modal — dropdown ile değer seçme ───
    // options: { title, subtitle, label, options (HTML string), hint, confirmText }
    // Promise<string|null> döner. null = iptal
    _showSelectModal({ title, subtitle, label, options, hint, confirmText }) {
        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000';
            overlay.innerHTML = `
                <div style="background:#fff;border-radius:8px;padding:24px;width:360px;max-width:90vw;box-shadow:0 8px 32px rgba(0,0,0,.2)">
                    <h3 style="margin:0 0 4px;font-size:16px">${title || 'Seçim'}</h3>
                    ${subtitle ? `<p style="margin:0 0 16px;font-size:13px;color:#6b7280">${subtitle}</p>` : '<div style="margin-bottom:16px"></div>'}
                    <label style="display:block;font-size:13px;font-weight:500;margin-bottom:6px">${label || 'Seçin'}</label>
                    <select id="_selModalSelect" style="width:100%;padding:8px;border:1px solid #d1d5db;border-radius:6px;font-size:14px;margin-bottom:8px">
                        ${options}
                    </select>
                    ${hint ? `<p style="font-size:12px;color:#6b7280;margin:0 0 16px">${hint}</p>` : '<div style="margin-bottom:16px"></div>'}
                    <div style="display:flex;gap:8px;justify-content:flex-end">
                        <button id="_selModalCancel" style="padding:8px 16px;border:1px solid #d1d5db;border-radius:6px;background:#fff;cursor:pointer">İptal</button>
                        <button id="_selModalConfirm" style="padding:8px 16px;border:none;border-radius:6px;background:#2563eb;color:#fff;cursor:pointer">${confirmText || 'Seç'}</button>
                    </div>
                </div>`;
            document.body.appendChild(overlay);
            const sel = overlay.querySelector('#_selModalSelect');
            overlay.querySelector('#_selModalConfirm').addEventListener('click', () => {
                document.body.removeChild(overlay);
                resolve(sel.value || null);
            });
            overlay.querySelector('#_selModalCancel').addEventListener('click', () => {
                document.body.removeChild(overlay);
                resolve(null);
            });
            overlay.addEventListener('click', e => {
                if (e.target === overlay) { document.body.removeChild(overlay); resolve(null); }
            });
        });
    }

    // Confirm-only modal (window.confirm yerine).
    // Promise<boolean> döner: true = onay, false = iptal/escape.
    // options: { title, message, confirmText, cancelText, variant }
    async showConfirmModal(options = {}) {
        const overlay = this.$('#promptModal');
        const form = this.$('#promptModalForm');
        const inputBlock = this.$('#promptModalInput');
        const labelEl = this.$('#promptModalLabel');
        const hintEl = this.$('#promptModalHint');
        const errorEl = this.$('#promptModalError');

        // Inputu geçici gizle
        if (inputBlock) inputBlock.style.display = 'none';
        if (labelEl) labelEl.style.display = 'none';
        if (errorEl) errorEl.classList.add('hidden');

        const result = await this.showPromptModal({
            title: options.title || 'Onay gerekli',
            subtitle: options.subtitle || '',
            label: '',
            hint: options.message || '',
            confirmText: options.confirmText || 'Onayla',
            cancelText: options.cancelText || 'İptal',
            variant: options.variant || 'default',
            required: false
        });

        // Stilleri geri yükle
        if (inputBlock) inputBlock.style.display = '';
        if (labelEl) labelEl.style.display = '';
        if (hintEl) hintEl.style.fontSize = '';

        return result !== null;
    }

    // ─── Tabs ───
    switchTab(name) {
        this.$$('.tab-btn').forEach(b => b.classList.remove('active'));
        this.$$('.tab-content').forEach(c => c.classList.remove('active'));
        const btn = document.querySelector(`.tab-btn[data-tab="${name}"]`);
        if (btn) btn.classList.add('active');
        const tab = document.querySelector(`#tab-${name}`);
        if (tab) tab.classList.add('active');
    }

    _bindTabs() {
        this.$$('.tab-btn').forEach(btn => {
            btn.addEventListener('click', () => this.switchTab(btn.dataset.tab));
        });
    }

    _bindControls() {
        const autoRefresh = this.$('#autoRefreshToggle');
        if (autoRefresh) {
            autoRefresh.addEventListener('change', e => {
                if (e.target.checked) this.startRefresh();
                else this.stopRefresh();
            });
        }

        const refreshBtn = this.$('#refreshBtn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.loadAll());
        }

        // Transcript modal — kapanma binding'leri
        const closeBtn = this.$('#transcriptModalClose');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => this.closeTranscriptModal());
        }
        const overlay = this.$('#transcriptModal');
        if (overlay) {
            // Overlay'a (modal dışı alan) tıklayınca kapat
            overlay.addEventListener('click', e => {
                if (e.target === overlay) this.closeTranscriptModal();
            });
        }
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') this.closeTranscriptModal();
        });

        // Analytics — session sidebar "Genel" butonu
        const aggregateBtn = this.$('#showAggregateBtn');
        if (aggregateBtn) {
            aggregateBtn.addEventListener('click', () => this.showAggregateAnalytics());
        }
    }

    // ─── Approvals ───
    renderApprovalCard(req) {
        const tpl = this.$('#approval-card-template');
        const node = tpl.content.cloneNode(true);

        node.querySelector('[data-tool]').textContent = req.toolName;
        node.querySelector('[data-id]').textContent = req.id;
        node.querySelector('[data-time]').textContent = this.formatTime(req.requestedAt);
        node.querySelector('[data-agent]').textContent = req.agentName || '—';
        node.querySelector('[data-query]').textContent = req.userQuery || '—';
        node.querySelector('[data-session]').textContent = req.sessionId?.slice(0, 12) + '…' || '—';
        node.querySelector('[data-params]').textContent =
            JSON.stringify(req.parameters || {}, null, 2);

        const approveBtn = node.querySelector('[data-approve]');
        const rejectBtn = node.querySelector('[data-reject]');

        approveBtn.addEventListener('click', async () => {
            // Yüksek riskli tool'larda gerekçe zorunlu (audit trail)
            const highRisk = AdminPanel.HIGH_RISK_TOOLS.has(req.toolName);
            const reason = await this.showPromptModal({
                title: highRisk ? 'Yüksek riskli işlem onayı' : 'Tool çağrısını onayla',
                subtitle: `${req.toolName} · ${req.id}`,
                label: 'Onay gerekçesi',
                placeholder: highRisk
                    ? 'Ör. müşteri telefon doğrulaması yapıldı, parametreler kontrol edildi'
                    : 'Kısa not (opsiyonel)',
                hint: highRisk
                    ? 'Bu yan etkili bir işlem; gerekçe denetim kaydı için zorunludur.'
                    : 'Gerekçe boş bırakılabilir; ileride denetim için eklemeniz önerilir.',
                confirmText: 'Onayla',
                required: highRisk,
                requiredMessage: 'Yüksek riskli işlemler için gerekçe zorunludur.',
                variant: highRisk ? 'warning' : 'default'
            });
            if (reason === null) return; // iptal

            approveBtn.disabled = true;
            rejectBtn.disabled = true;
            approveBtn.textContent = 'Onaylanıyor…';
            try {
                await this.fetchJson(AdminPanel.API.approve(req.id), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ decidedBy: 'admin', reason })
                });
                await this.loadAll();
            } catch (e) {
                approveBtn.disabled = false;
                rejectBtn.disabled = false;
                approveBtn.textContent = '✓ Onayla';
                alert('Onay başarısız: ' + e.message);
            }
        });

        rejectBtn.addEventListener('click', async () => {
            const reason = await this.showPromptModal({
                title: 'Tool çağrısını reddet',
                subtitle: `${req.toolName} · ${req.id}`,
                label: 'Red sebebi',
                placeholder: 'Ör. parametreler eksik veya hatalı',
                hint: 'Sebep opsiyoneldir, ama ileride denetim için eklemeniz önerilir.',
                confirmText: 'Reddet',
                variant: 'danger'
            });
            if (reason === null) return;

            approveBtn.disabled = true;
            rejectBtn.disabled = true;
            rejectBtn.textContent = 'Reddediliyor…';
            try {
                await this.fetchJson(AdminPanel.API.reject(req.id), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ decidedBy: 'admin', reason })
                });
                await this.loadAll();
            } catch (e) {
                approveBtn.disabled = false;
                rejectBtn.disabled = false;
                rejectBtn.textContent = '✗ Reddet';
                alert('Reddetme başarısız: ' + e.message);
            }
        });

        return node;
    }

    renderHistoryApproval(req) {
        const tpl = this.$('#history-approval-template');
        const node = tpl.content.cloneNode(true);
        node.querySelector('[data-tool]').textContent = req.toolName;
        const st = node.querySelector('[data-status]');
        st.textContent = req.status;
        st.className = 'status-tag ' + this.statusClassName(req.status);
        node.querySelector('[data-id]').textContent = req.id;
        node.querySelector('[data-time]').textContent = this.formatTime(req.decidedAt || req.requestedAt);
        node.querySelector('[data-decided-by]').textContent = req.decidedBy || 'system';
        node.querySelector('[data-decision-reason]').textContent = req.decisionReason || '(gerekçe yok)';
        return node;
    }

    async loadApprovals() {
        if (this.isAgent) return; // Agent onay kuyruğuna erişemez
        try {
            const pending = await this.fetchJson(AdminPanel.API.pendingApprovals);
            const list = this.$('#approvalsList');
            list.innerHTML = '';
            if (pending.length === 0) {
                list.innerHTML = '<div class="empty-state">Onay bekleyen istek yok.</div>';
            } else {
                pending.forEach(req => list.appendChild(this.renderApprovalCard(req)));
            }
            this.$('#pendingApprovalsBadge').textContent = pending.length;

            // History
            const recent = await this.fetchJson(AdminPanel.API.recentApprovals);
            const historyDecided = recent.filter(r => r.status !== 'pending');
            const historyList = this.$('#historyApprovalsList');
            historyList.innerHTML = '';
            if (historyDecided.length === 0) {
                historyList.innerHTML = '<div class="empty-state">Henüz karar kaydı yok.</div>';
            } else {
                historyDecided.forEach(r => historyList.appendChild(this.renderHistoryApproval(r)));
            }
        } catch (e) {
            console.error('Approvals load failed:', e);
        }
    }

    // ─── Escalations ───
    renderEscalationCard(req) {
        const tpl = this.$('#escalation-card-template');
        const node = tpl.content.cloneNode(true);

        const st = node.querySelector('[data-status]');
        st.textContent = req.status;
        st.className = 'status-tag ' + this.statusClassName(req.status);
        node.querySelector('[data-id]').textContent = req.id;
        node.querySelector('[data-time]').textContent = this.formatTime(req.createdAt);
        node.querySelector('[data-agent]').textContent = req.agentName || '—';
        node.querySelector('[data-assigned]').textContent = req.assignedTo || '—';
        node.querySelector('[data-reason]').textContent = req.reason || '—';
        node.querySelector('[data-query]').textContent = req.userQuery || '—';

        const contextLines = [];
        if (req.missingContext?.length) {
            contextLines.push('Eksik bağlam:');
            req.missingContext.forEach(c => contextLines.push('  - ' + c));
        }
        if (req.responseSummary) {
            contextLines.push('');
            contextLines.push('Yanıt özeti:');
            contextLines.push(req.responseSummary);
        }
        node.querySelector('[data-context]').textContent =
            contextLines.join('\n') || '(bağlam yok)';

        const ackBtn = node.querySelector('[data-ack]');
        const takeoverBtn = node.querySelector('[data-takeover]');
        const resolveBtn = node.querySelector('[data-resolve]');
        const dismissBtn = node.querySelector('[data-dismiss]');

        // Agent "Atama Yap" butonunu görmemeli — sadece admin atama yapabilir
        if (this.isAgent) {
            ackBtn.style.display = 'none';
        }

        // Sohbet zaten devralınmışsa devral/üstlen butonları anlamsız
        const alreadyTakenOver = req.sessionId
            && this.takenOverSessionIds?.has(req.sessionId);

        // Acknowledged olmuşsa ack butonu disabled
        if (req.status === 'acknowledged' || alreadyTakenOver) {
            ackBtn.disabled = true;
            ackBtn.textContent = alreadyTakenOver ? '✓ Devralındı' : '✓ Atandı';
            if (alreadyTakenOver) ackBtn.title = 'Sohbet zaten bir temsilci tarafından devralındı';
        }

        // Session bilgisi yoksa veya sohbet zaten devralınmışsa devral butonunu kapat
        if (!req.sessionId) {
            takeoverBtn.disabled = true;
            takeoverBtn.title = 'Session bilgisi yok — devralınamaz';
        } else if (alreadyTakenOver) {
            takeoverBtn.disabled = true;
            takeoverBtn.textContent = '✓ Devralındı';
            takeoverBtn.title = 'Bu sohbet zaten devralınmış durumda. "Aktif Sohbetler" sekmesinden devam edin.';
        }

        takeoverBtn.addEventListener('click', async () => {
            let agent;
            if (this.isAgent) {
                agent = this.linkedAgentId || this.userRole;
            } else {
                agent = await this.showPromptModal({
                    title: 'Sohbeti devral',
                    subtitle: `Eskalasyon ${req.id} · oturum ${(req.sessionId || '').slice(0, 12)}…`,
                    label: 'Temsilci adı',
                    placeholder: 'Ör. ayşe.yılmaz',
                    hint: 'Bu isim müşteriye "temsilciniz X sizinle iletişime geçti" olarak gösterilir.',
                    confirmText: 'Devral',
                    defaultValue: 'admin',
                    required: true,
                    requiredMessage: 'Devralabilmek için temsilci adı gerekli.'
                });
                if (!agent) return;
            }
            takeoverBtn.disabled = true;
            try {
                await this.fetchJson(AdminPanel.API.takeover(req.sessionId), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ humanAgent: agent })
                });
                // Aktif sohbetler tab'ına geç + paneli aç
                this.switchTab('chats');
                await this.openChatPanel(req.sessionId, agent);
                await this.loadAll();
            } catch (e) {
                takeoverBtn.disabled = false;
                alert('Devralma başarısız: ' + e.message);
            }
        });

        ackBtn.addEventListener('click', async () => {
            let assignedTo;
            if (this.isAgent) {
                // Agent kendi üzerine alır — modal sorma
                assignedTo = this.linkedAgentId;
                if (!assignedTo) {
                    alert('Agent ID bulunamadı. Lütfen yeniden giriş yapın.');
                    return;
                }
            } else {
                // Admin başka agent'a atar — önce agent listesini çek
                if (!this._agentList || this._agentList.length === 0) {
                    try {
                        const resp = await this.fetchJson('/agents');
                        // GET /agents → { count, items } döner
                        this._agentList = Array.isArray(resp) ? resp : (resp.items ?? []);
                    } catch { this._agentList = []; }
                }
                const agents = this._agentList;
                if (agents.length > 0) {
                    // Seçenek listesi: "DisplayName (id)"
                    const options = agents.map(a =>
                        `<option value="${a.id}"${!a.isActive ? ' style="color:#9ca3af"' : ''}>${a.displayName} — ${a.id}${a.isActive ? '' : ' (pasif)'}</option>`
                    ).join('');
                    assignedTo = await this._showSelectModal({
                        title: 'Temsilci Ata',
                        subtitle: `${req.id} · ${req.agentName || '—'}`,
                        label: 'Temsilci seç',
                        options,
                        hint: 'Müşteri "temsilcimiz [isim] talebinizi üstlendi" bilgilendirmesi alır.',
                        confirmText: 'Atama Yap'
                    });
                } else {
                    assignedTo = await this.showPromptModal({
                        title: 'Temsilci Ata',
                        subtitle: `${req.id} · ${req.agentName || '—'}`,
                        label: 'Atanacak temsilci (agent ID)',
                        placeholder: 'Ör. agent-jdoe',
                        hint: 'Müşteri "temsilcimiz [isim] talebinizi üstlendi" bilgilendirmesi alır.',
                        confirmText: 'Atama Yap',
                        required: true,
                        requiredMessage: 'Atama yapmak için temsilci ID gerekli.'
                    });
                }
                if (!assignedTo) return;
            }
            try {
                await this.fetchJson(AdminPanel.API.acknowledge(req.id), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ assignedTo })
                });
                await this.loadAll();
            } catch (e) { alert('İşlem başarısız: ' + e.message); }
        });

        resolveBtn.addEventListener('click', async () => {
            const resolution = await this.showPromptModal({
                title: 'Eskalasyonu çöz',
                subtitle: `${req.id} · ${req.agentName || '—'}`,
                label: 'Çözüm açıklaması',
                placeholder: 'Müşteriyle ne yapıldı, sonuç ne oldu?',
                hint: 'Bu metin denetim kaydına yazılır; müşteriye otomatik gösterilmez.',
                confirmText: 'Çöz',
                required: true,
                requiredMessage: 'Çözüm açıklaması zorunludur.'
            });
            if (!resolution) return;
            try {
                await this.fetchJson(AdminPanel.API.resolve(req.id), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ resolution, assignedTo: req.assignedTo || 'admin' })
                });
                await this.loadAll();
            } catch (e) { alert('İşlem başarısız: ' + e.message); }
        });

        dismissBtn.addEventListener('click', async () => {
            const resolution = await this.showPromptModal({
                title: 'Eskalasyonu reddet',
                subtitle: `${req.id} · yanlış eskalasyon olarak kapatılacak`,
                label: 'Dismiss sebebi',
                placeholder: 'Ör. bot gereksiz yere eskalasyon üretti',
                hint: 'Bu işlem geri alınamaz; eskalasyon kapanır ve müşteriye bildirim gitmez.',
                confirmText: 'Reddet',
                variant: 'danger',
                defaultValue: 'yanlış eskalasyon'
            });
            if (resolution === null) return;
            try {
                await this.fetchJson(AdminPanel.API.dismiss(req.id), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ resolution })
                });
                await this.loadAll();
            } catch (e) { alert('İşlem başarısız: ' + e.message); }
        });

        const replanBtn = node.querySelector('[data-replan]');
        if (replanBtn) {
            // Session bilgisi yoksa replan anlamsız
            if (!req.sessionId) {
                replanBtn.disabled = true;
                replanBtn.title = 'Session bilgisi yok — yeniden planlanamaz';
            }

            replanBtn.addEventListener('click', async () => {
                const note = await this.showPromptModal({
                    title: 'Bot\'u yeniden planlatmak istediğine emin misin?',
                    subtitle: `${req.id} · ${req.agentName || '—'}`,
                    label: 'Bot\'a not (opsiyonel — müşteriye gösterilmez)',
                    placeholder: 'Ör. şikayet ajanına yönlendir, sipariş sorgulamadan kaçın',
                    hint: 'Bu not yalnızca PlanningAgent\'a iletilir; müşteri yalnızca “Talebinizi tekrar değerlendiriyoruz” sistem mesajını görür. Eskalasyon otomatik resolve edilir.',
                    confirmText: 'Yeniden Planla',
                    variant: 'warning'
                });
                if (note === null) return;
                try {
                    await this.fetchJson(AdminPanel.API.replanEscalation(req.id), {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            requestedBy: 'admin',
                            note: note || null
                        })
                    });
                    await this.loadAll();
                } catch (e) { alert('Yeniden planlama başarısız: ' + e.message); }
            });
        }

        return node;
    }

    renderHistoryEscalation(req) {
        const tpl = this.$('#history-escalation-template');
        const node = tpl.content.cloneNode(true);
        const st = node.querySelector('[data-status]');
        st.textContent = req.status;
        st.className = 'status-tag ' + this.statusClassName(req.status);
        node.querySelector('[data-id]').textContent = req.id;
        node.querySelector('[data-time]').textContent = this.formatTime(req.resolvedAt || req.createdAt);
        node.querySelector('[data-agent]').textContent = req.agentName || '—';
        node.querySelector('[data-resolution]').textContent = req.resolution || '(çözüm yok)';
        return node;
    }

    /**
     * Eskalasyonlar tab'ındaki sol sidebar için kompakt kart.
     * "Konuşmayı Gör" butonuyla session transcript modal'ını açar.
     */
    renderClosedEscalationCompact(req) {
        const tpl = this.$('#closed-escalation-compact-template');
        const node = tpl.content.cloneNode(true);
        const st = node.querySelector('[data-status]');
        st.textContent = req.status;
        st.className = 'status-tag ' + this.statusClassName(req.status);
        node.querySelector('[data-id]').textContent = req.id;
        node.querySelector('[data-time]').textContent = this.formatTime(req.resolvedAt || req.createdAt);
        node.querySelector('[data-agent]').textContent = req.agentName || '—';
        node.querySelector('[data-reason]').textContent = req.reason || '—';
        node.querySelector('[data-resolution]').textContent = req.resolution || '(çözüm notu yok)';

        const viewBtn = node.querySelector('[data-view-transcript]');
        if (viewBtn) {
            if (!req.sessionId) {
                viewBtn.disabled = true;
                viewBtn.title = 'Session bilgisi yok — konuşma görüntülenemez';
            } else {
                viewBtn.addEventListener('click', () => this.openTranscriptModal(req));
            }
        }
        return node;
    }

    /**
     * Read-only transcript modal'ını aç ve session geçmişini yükle.
     */
    async openTranscriptModal(esc) {
        const overlay = this.$('#transcriptModal');
        const title = this.$('#transcriptModalTitle');
        const subtitle = this.$('#transcriptModalSubtitle');
        const messagesEl = this.$('#transcriptMessages');
        if (!overlay || !messagesEl) return;

        title.textContent = `Konuşma — ${esc.id}`;
        const parts = [];
        if (esc.agentName) parts.push(`Agent: ${esc.agentName}`);
        if (esc.status) parts.push(`Status: ${esc.status}`);
        if (esc.sessionId) parts.push(`Session: ${esc.sessionId.slice(0, 12)}…`);
        subtitle.textContent = parts.join(' · ');

        messagesEl.innerHTML = '<div class="empty-state">Yükleniyor…</div>';
        overlay.classList.remove('hidden');

        try {
            const history = await this.fetchJson(AdminPanel.API.chatHistory(esc.sessionId));
            messagesEl.innerHTML = '';
            if (!history || history.length === 0) {
                messagesEl.innerHTML = '<div class="empty-state">Bu session için kayıtlı mesaj yok.</div>';
            } else {
                history.forEach(msg => messagesEl.appendChild(this.renderChatBubble(msg)));
                this.scrollMessagesToBottom(messagesEl);
            }
        } catch (e) {
            messagesEl.innerHTML =
                `<div class="empty-state">Konuşma yüklenemedi: ${this.escapeHtml(e.message)}</div>`;
        }
    }

    closeTranscriptModal() {
        const overlay = this.$('#transcriptModal');
        if (!overlay) return;
        overlay.classList.add('hidden');
        const messagesEl = this.$('#transcriptMessages');
        if (messagesEl) messagesEl.innerHTML = '';
    }

    // API yanıtını düz diziye normalize et ({ items } veya array olabilir)
    _normalizeList(resp) {
        if (Array.isArray(resp)) return resp;
        if (resp && Array.isArray(resp.items)) return resp.items;
        return [];
    }

    async loadEscalations() {
        try {
            const open = this._normalizeList(await this.fetchJson(AdminPanel.API.openEscalations));
            const list = this.$('#escalationsList');
            list.innerHTML = '';

            if (open.length === 0) {
                list.innerHTML = '<div class="empty-state">Açık eskalasyon yok.</div>';
            } else if (this.isAgent && this.linkedAgentId) {
                // Agent görünümü: "Benim" ve "Diğer" bölümleri
                const mine = open.filter(r =>
                    r.assignedTo === this.linkedAgentId);
                // Başka agente atananları gizle — sadece atanmamışları göster
                const others = open.filter(r =>
                    !r.assignedTo || r.assignedTo === '');

                if (mine.length > 0) {
                    const sec = document.createElement('div');
                    sec.className = 'escalation-section';
                    sec.innerHTML = `<div class="escalation-section-title mine">
                        <span>📌 Bana Atanan (${mine.length})</span>
                    </div>`;
                    mine.forEach(r => sec.appendChild(this.renderEscalationCard(r)));
                    list.appendChild(sec);
                }
                if (others.length > 0) {
                    const sec = document.createElement('div');
                    sec.className = 'escalation-section';
                    sec.innerHTML = `<div class="escalation-section-title others">
                        <span>📋 Diğer Eskalasyonlar (${others.length})</span>
                    </div>`;
                    others.forEach(r => sec.appendChild(this.renderEscalationCard(r)));
                    list.appendChild(sec);
                }
                if (mine.length === 0 && others.length === 0) {
                    list.innerHTML = '<div class="empty-state">Açık eskalasyon yok.</div>';
                }
            } else {
                open.forEach(req => list.appendChild(this.renderEscalationCard(req)));
            }
            this.$('#openEscalationsBadge').textContent = open.length;

            // Kapanmış eskalasyonlar — Eskalasyonlar tab'ı sol sidebar + Geçmiş tab'ı
            const recent = this._normalizeList(await this.fetchJson(AdminPanel.API.recentEscalations));
            const closed = recent.filter(r =>
                r.status === 'resolved' || r.status === 'dismissed');

            // Sol sidebar (Eskalasyonlar tab'ı)
            const sidebar = this.$('#closedEscalationsList');
            if (sidebar) {
                sidebar.innerHTML = '';
                if (closed.length === 0) {
                    sidebar.innerHTML = '<div class="empty-state">Henüz kapanmış eskalasyon yok.</div>';
                } else {
                    closed.forEach(r => sidebar.appendChild(this.renderClosedEscalationCompact(r)));
                }
                const badge = this.$('#closedEscalationsBadge');
                if (badge) badge.textContent = closed.length;
            }

            // Geçmiş tab'ı (mevcut)
            const historyList = this.$('#historyEscalationsList');
            historyList.innerHTML = '';
            if (closed.length === 0) {
                historyList.innerHTML = '<div class="empty-state">Henüz çözülmüş eskalasyon yok.</div>';
            } else {
                closed.forEach(r => historyList.appendChild(this.renderHistoryEscalation(r)));
            }
        } catch (e) {
            console.error('Escalations load failed:', e);
        }
    }

    // ─── Active Chats (HITL Live Takeover) ───
    // ─── Sentiment badge helper ───
    static SENTIMENT_LABELS = {
        positive: '😊 Olumlu',
        neutral: '😐 Nötr',
        negative: '😟 Olumsuz',
        angry: '😡 Kızgın'
    };

    _applySentimentBadge(el, sentiment, score) {
        if (!el) return;
        const label = AdminPanel.SENTIMENT_LABELS[sentiment] || '😐 Nötr';
        el.textContent = label;
        el.className = `sentiment-badge sentiment-${sentiment || 'neutral'}`;
        el.title = `Duygu skoru: ${((score ?? 0.5) * 100).toFixed(0)}%`;
    }

    renderActiveChatCard(state) {
        const tpl = this.$('#active-chat-card-template');
        const node = tpl.content.cloneNode(true);

        node.querySelector('[data-sid]').textContent =
            (state.sessionId || '').slice(0, 12) + '…';
        node.querySelector('[data-time]').textContent = this.formatTime(state.enteredAt);
        node.querySelector('[data-agent]').textContent = state.humanAgent || 'admin';
        node.querySelector('[data-count]').textContent = state.messageCount ?? 0;

        // Sentiment badge — async olarak yükle
        const sentimentEl = node.querySelector('[data-sentiment]');
        if (state.sessionId) {
            this.fetchJson(AdminPanel.API.sessionSentiment(state.sessionId))
                .then(d => this._applySentimentBadge(sentimentEl, d.sentiment, d.score))
                .catch(() => {});
        }

        const openBtn = node.querySelector('[data-open]');
        openBtn.addEventListener('click', () => {
            this.openChatPanel(state.sessionId, state.humanAgent);
        });

        return node;
    }

    async loadActiveChats() {
        try {
            const active = await this.fetchJson(AdminPanel.API.activeChats);
            this.$('#activeChatsBadge').textContent = active.length;

            // Devralınmış session'ları cache'le — eskalasyon kartları buna bakacak
            this.takenOverSessionIds = new Set(
                active.filter(s => s.humanAgent).map(s => s.sessionId)
            );

            const list = this.$('#activeChatsList');
            list.innerHTML = '';
            if (active.length === 0) {
                list.innerHTML = '<div class="empty-state">Aktif sohbet yok.</div>';
                // Panel açıkken session Bot'a dönmüşse paneli kapat
                if (this.chatPanelState.sessionId
                    && !active.some(s => s.sessionId === this.chatPanelState.sessionId)) {
                    this.closeChatPanel();
                }
            } else {
                active.forEach(s => list.appendChild(this.renderActiveChatCard(s)));
            }
        } catch (e) {
            console.error('Active chats load failed:', e);
        }
    }

    // ─── Chat Panel (inline canlı sohbet) ───
    renderChatBubble(msg) {
        const wrap = document.createElement('div');
        wrap.className = 'chat-bubble ' + (msg.sender || '').toLowerCase();
        const text = this.escapeHtml(msg.text || '');
        const meta = msg.humanAgent
            ? `${this.escapeHtml(msg.humanAgent)} · ${this.formatTime(msg.timestamp)}`
            : this.formatTime(msg.timestamp);
        wrap.innerHTML = `${text}<span class="chat-bubble-meta">${meta}</span>`;
        return wrap;
    }

    scrollMessagesToBottom(messagesEl) {
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    async openChatPanel(sessionId, humanAgent) {
        this.closeChatPanel(); // Önceki panel varsa kapat

        const host = this.$('#chatPanelHost');
        const tpl = this.$('#chat-panel-template');
        const node = tpl.content.cloneNode(true);

        // Template'i DOM'a koymadan önce referansları al
        const panel = node.querySelector('.chat-panel');
        const sidEl = node.querySelector('[data-sid]');
        const agentEl = node.querySelector('[data-agent]');
        const enteredEl = node.querySelector('[data-entered]');
        const sentimentEl = node.querySelector('[data-sentiment]');
        const messagesEl = node.querySelector('[data-messages]');
        const composer = node.querySelector('[data-composer]');
        const textarea = node.querySelector('[data-textarea]');
        const releaseBtn = node.querySelector('[data-release]');
        const replanChatBtn = node.querySelector('[data-replan-chat]');

        sidEl.textContent = sessionId;
        agentEl.textContent = humanAgent || 'admin';
        enteredEl.textContent = this.formatTime(new Date().toISOString());

        // Sentiment badge — ilk yükleme + 5sn periyodik güncelleme
        const refreshSentiment = () => {
            this.fetchJson(AdminPanel.API.sessionSentiment(sessionId))
                .then(d => this._applySentimentBadge(sentimentEl, d.sentiment, d.score))
                .catch(() => {});
        };
        refreshSentiment();
        this.chatPanelState.sentimentTimer = setInterval(refreshSentiment, 5000);

        host.innerHTML = '';
        host.appendChild(node);

        this.chatPanelState.sessionId = sessionId;
        this.chatPanelState.humanAgent = humanAgent || 'admin';

        // Geçmişi yükle
        try {
            const history = await this.fetchJson(AdminPanel.API.chatHistory(sessionId));
            history.forEach(msg => messagesEl.appendChild(this.renderChatBubble(msg)));
            this.scrollMessagesToBottom(messagesEl);
        } catch (e) {
            console.warn('History load failed:', e);
        }

        // Canlı SSE aboneliği (bu session'ın admin kanalı) — JWT query param ile
        const es = new EventSource(window.Auth.eventSourceUrl(AdminPanel.API.subscribeChat(sessionId)));
        this.chatPanelState.eventSource = es;

        es.addEventListener('bridge_message', (ev) => {
            try {
                const msg = JSON.parse(ev.data);
                messagesEl.appendChild(this.renderChatBubble(msg));
                this.scrollMessagesToBottom(messagesEl);
                // Yeni mesaj geldiğinde sentiment badge'i güncelle
                refreshSentiment();
            } catch (e) { console.warn('parse failed', e); }
        });
        es.addEventListener('error', () => {
            // Reconnect otomatik — bir şey yapmaya gerek yok
        });

        // Composer: Enter = gönder, Shift+Enter = yeni satır
        textarea.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                composer.requestSubmit();
            }
        });

        composer.addEventListener('submit', async (e) => {
            e.preventDefault();
            const text = textarea.value.trim();
            if (!text) return;
            textarea.value = '';
            try {
                await this.fetchJson(AdminPanel.API.sendChat(sessionId), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        text,
                        humanAgent: this.chatPanelState.humanAgent
                    })
                });
                // Kendi mesajını optimistik olarak DA ekle — bridge_message event'i
                // Kendi admin subscriber'ına gelmez (admin → user kanalı), bu yüzden
                // Burada local render ediyoruz.
                messagesEl.appendChild(this.renderChatBubble({
                    sender: 'admin',
                    humanAgent: this.chatPanelState.humanAgent,
                    text,
                    timestamp: new Date().toISOString()
                }));
                this.scrollMessagesToBottom(messagesEl);
                textarea.focus();
            } catch (err) {
                alert('Gönderme başarısız: ' + err.message);
            }
        });

        releaseBtn.addEventListener('click', async () => {
            const ok = await this.showConfirmModal({
                title: 'Sohbeti sonlandır',
                message: 'Aktif sohbeti kapatmak istediğine emin misin? Session Bot moduna geri dönecek ve müşteri tekrar bot ile yazışmaya başlayacak.',
                confirmText: 'Evet, sonlandır',
                cancelText: 'Vazgeç',
                variant: 'warning'
            });
            if (!ok) return;
            try {
                await this.fetchJson(AdminPanel.API.release(sessionId), { method: 'POST' });
                this.closeChatPanel();
                await this.loadAll();
            } catch (e) {
                alert('Sonlandırma başarısız: ' + e.message);
            }
        });

        if (replanChatBtn) {
            replanChatBtn.addEventListener('click', async () => {
                const note = await this.showPromptModal({
                    title: 'Bot\'u yeniden planlat',
                    subtitle: `${sessionId.slice(0, 12)}…`,
                    label: 'Bot\'a not (opsiyonel — müşteriye gösterilmez)',
                    placeholder: 'Ör. müşteriyi şikayet kaydı oluşturmaya yönlendirdim',
                    hint: 'Bu not yalnızca PlanningAgent\'a iletilir. Müşteri sadece “Talebinizi tekrar değerlendiriyoruz” bildirimini görür. Aktif sohbet kapanır ve bir sonraki mesaj bot\'a gider.',
                    confirmText: 'Yeniden Planla',
                    variant: 'warning'
                });
                if (note === null) return; // iptal
                try {
                    await this.fetchJson(AdminPanel.API.replanChat(sessionId), {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            requestedBy: 'admin',
                            note: note || null
                        })
                    });
                    this.closeChatPanel();
                    await this.loadAll();
                } catch (e) {
                    alert('Yeniden planlama başarısız: ' + e.message);
                }
            });
        }

        textarea.focus();
    }

    closeChatPanel() {
        if (this.chatPanelState.eventSource) {
            try { this.chatPanelState.eventSource.close(); } catch {}
            this.chatPanelState.eventSource = null;
        }
        if (this.chatPanelState.sentimentTimer) {
            clearInterval(this.chatPanelState.sentimentTimer);
            this.chatPanelState.sentimentTimer = null;
        }
        this.chatPanelState.sessionId = null;
        this.chatPanelState.humanAgent = null;

        const host = this.$('#chatPanelHost');
        if (host) {
            host.innerHTML =
                '<div class="empty-state">Sol taraftan bir session seç ya da bir eskalasyondan devral.</div>';
        }
    }

    // ─── Analytics Dashboard ───
    async loadAnalytics() {
        if (this.isAgent) return; // Agent analytics'e erişemez
        try {
            // Session listesini yükle (sidebar)
            this.loadAnalyticsSessionList();

            // Eğer session seçiliyse session analytics yükle
            if (this.analyticsSelectedSessionId) {
                await this.loadSessionAnalytics(this.analyticsSelectedSessionId);
                return;
            }

            // Aggregate view
            const d = await this.fetchJson(AdminPanel.API.analyticsDashboard);
            // Summary cards
            this.$('#statTotalSessions').textContent = d.totalSessions;
            this.$('#statTotalMessages').textContent = d.totalMessages;
            this.$('#statAverageRating').textContent = d.averageRating > 0 ? `${d.averageRating} ★` : '—';
            this.$('#statTotalRatings').textContent = d.totalRatings;
            this.$('#statAvgMessages').textContent = d.averageSessionMessages;

            // Rating distribution (horizontal bars)
            this.renderRatingDistribution(d.ratingDistribution, d.totalRatings);

            // Approval stats
            this.renderApprovalStats(d);

            // Escalation stats
            this.renderEscalationStats(d);

            // Intent distribution
            this.renderDistributionList('#intentDistribution', d.intentDistribution, this.intentLabel);

            // Phase distribution
            this.renderDistributionList('#phaseDistribution', d.phaseDistribution, this.phaseLabel);

            // Recent ratings
            this.renderRecentRatings(d.recentRatings);

            // Sentiment stats
            this.$('#statAvgSentiment').textContent = d.averageSentimentScore > 0
                ? `${(d.averageSentimentScore * 100).toFixed(0)}%` : '—';
            this.$('#statNegativeSessions').textContent = d.negativeSessionCount;
            this.$('#statSentimentAlerts').textContent = d.sentimentAlertCount;

            // Sentiment distribution
            this.renderDistributionList('#sentimentDistribution', d.sentimentDistribution, this.sentimentLabel);
        } catch (e) {
            console.error('Analytics load failed:', e);
        }
    }

    async loadAnalyticsSessionList() {
        const listEl = this.$('#analyticsSessionList');
        if (!listEl) return;
        try {
            const sessions = await this.fetchJson(AdminPanel.API.sessionsList);
            if (!sessions.length) {
                listEl.innerHTML = '<div class="empty-state small-empty">Henüz oturum yok.</div>';
                return;
            }
            listEl.innerHTML = sessions.map(s => {
                const active = s.sessionId === this.analyticsSelectedSessionId ? ' active' : '';
                const title = this.escapeHtml(s.title || '(boş)');
                const shortId = (s.sessionId || '').substring(0, 8);
                const time = this.formatTime(s.lastActivity);
                return `
                    <div class="analytics-session-item${active}" data-sid="${s.sessionId}">
                        <div class="analytics-session-title">${title}</div>
                        <div class="analytics-session-meta">
                            <span>${s.messageCount} mesaj</span>
                            <span>•</span>
                            <span>${time}</span>
                        </div>
                        <code class="analytics-session-id">${shortId}…</code>
                    </div>`;
            }).join('');

            listEl.querySelectorAll('.analytics-session-item').forEach(el => {
                el.addEventListener('click', () => this.selectAnalyticsSession(el.dataset.sid));
            });
        } catch (e) {
            listEl.innerHTML = '<div class="empty-state small-empty">Yüklenemedi.</div>';
        }
    }

    selectAnalyticsSession(sid) {
        this.analyticsSelectedSessionId = sid;
        // Toggle views
        const agg = this.$('#analyticsAggregateView');
        const ses = this.$('#analyticsSessionView');
        if (agg) agg.classList.add('hidden');
        if (ses) ses.classList.remove('hidden');
        // Update sidebar active state
        this.$$('.analytics-session-item').forEach(el => {
            el.classList.toggle('active', el.dataset.sid === sid);
        });
        this.loadSessionAnalytics(sid);
    }

    showAggregateAnalytics() {
        this.analyticsSelectedSessionId = null;
        const agg = this.$('#analyticsAggregateView');
        const ses = this.$('#analyticsSessionView');
        if (agg) agg.classList.remove('hidden');
        if (ses) ses.classList.add('hidden');
        this.$$('.analytics-session-item').forEach(el => el.classList.remove('active'));
        this.loadAnalytics();
    }

    async loadSessionAnalytics(sid) {
        const header = this.$('#sessionAnalyticsHeader');
        const body = this.$('#sessionAnalyticsBody');
        if (!header || !body) return;

        try {
            const d = await this.fetchJson(AdminPanel.API.sessionAnalytics(sid));
            this.renderSessionAnalytics(d, header, body);
        } catch (e) {
            header.innerHTML = '';
            body.innerHTML = `<div class="empty-state">Session analytics yüklenemedi: ${e.message}</div>`;
        }
    }

    renderSessionAnalytics(d, header, body) {
        const sentimentEmoji = { positive: '😊', neutral: '😐', negative: '😟', angry: '😡' };
        const emoji = sentimentEmoji[d.sentiment] || '😐';
        const scorePct = Math.round(d.sentimentScore * 100);
        const stars = d.rating ? '★'.repeat(d.rating.stars) + '☆'.repeat(5 - d.rating.stars) : null;

        header.innerHTML = `
            <div class="session-analytics-title-row">
                <div>
                    <h3>Oturum Detayı</h3>
                    <code class="session-id">${this.escapeHtml(d.sessionId)}</code>
                </div>
                <span class="sentiment-badge sentiment-${d.sentiment || 'neutral'}">${emoji} ${d.sentiment || 'neutral'} (${scorePct}%)</span>
            </div>
            <div class="session-analytics-cards">
                <div class="sa-card"><div class="sa-val">${d.messageCount}</div><div class="sa-lbl">Mesaj</div></div>
                <div class="sa-card"><div class="sa-val">${d.turnCount}</div><div class="sa-lbl">Tur</div></div>
                <div class="sa-card"><div class="sa-val">${this.escapeHtml(d.phase || '—')}</div><div class="sa-lbl">Faz</div></div>
                <div class="sa-card"><div class="sa-val">${this.escapeHtml(d.currentIntent || '—')}</div><div class="sa-lbl">Niyet</div></div>
                ${d.customerId ? `<div class="sa-card"><div class="sa-val">${this.escapeHtml(d.customerId)}</div><div class="sa-lbl">Müşteri</div></div>` : ''}
                ${stars ? `<div class="sa-card"><div class="sa-val sa-stars">${stars}</div><div class="sa-lbl">Puan</div></div>` : ''}
            </div>`;

        let sections = '';

        // Sentiment Timeline
        if (d.sentimentTimeline?.length) {
            sections += this._renderSentimentTimeline(d.sentimentTimeline, d.consecutiveNegativeTurns);
        }

        // Rating feedback
        if (d.rating?.feedback) {
            sections += `<div class="sa-section">
                <h4>💬 Müşteri Yorumu</h4>
                <div class="recent-rating-feedback">${this.escapeHtml(d.rating.feedback)}</div>
            </div>`;
        }

        // Approvals
        if (d.totalApprovals > 0) {
            sections += this._renderSessionApprovals(d);
        }

        // Escalations
        if (d.totalEscalations > 0) {
            sections += this._renderSessionEscalations(d);
        }

        // Collected Info
        if (d.collectedInfo && Object.keys(d.collectedInfo).length > 0) {
            const rows = Object.entries(d.collectedInfo).map(([k, v]) =>
                `<div class="sa-info-row"><span class="sa-info-key">${this.escapeHtml(k)}</span><span>${this.escapeHtml(v)}</span></div>`
            ).join('');
            sections += `<div class="sa-section"><h4>📋 Toplanan Bilgiler</h4>${rows}</div>`;
        }

        body.innerHTML = sections || '<div class="empty-state">Bu oturum için detay bulunamadı.</div>';
    }

    _renderSentimentTimeline(timeline, consecutiveNeg) {
        const bars = timeline.map(e => {
            const h = Math.max(8, Math.round(e.score * 100));
            const color = e.score >= 0.7 ? '#22c55e' : e.score >= 0.4 ? '#eab308' : '#ef4444';
            const emoji = { positive: '😊', neutral: '😐', negative: '😟', angry: '😡' }[e.label] || '😐';
            return `<div class="st-bar-wrap" title="Tur ${e.turn}: ${e.label} (${Math.round(e.score*100)}%)">
                <div class="st-bar" style="height:${h}%;background:${color}"></div>
                <span class="st-label">${emoji}</span>
                <span class="st-turn">T${e.turn}</span>
            </div>`;
        }).join('');

        const warn = consecutiveNeg >= 3
            ? `<div class="sa-warn">⚠️ Ardışık ${consecutiveNeg} olumsuz tur — otomatik eskalasyon tetiklenebilir</div>`
            : '';

        return `<div class="sa-section">
            <h4>📈 Duygu Zaman Çizelgesi</h4>
            ${warn}
            <div class="st-chart">${bars}</div>
        </div>`;
    }

    _renderSessionApprovals(d) {
        const items = d.approvalDetails.map(a => `
            <div class="sa-detail-item">
                <code>${this.escapeHtml(a.toolName)}</code>
                <span class="status-tag ${this.statusClassName(a.status)}">${a.status}</span>
                ${a.decidedBy ? `<span class="muted">by ${this.escapeHtml(a.decidedBy)}</span>` : ''}
                <time class="muted">${this.formatTime(a.requestedAt)}</time>
            </div>`).join('');
        return `<div class="sa-section">
            <h4>✅ Onay Geçmişi (${d.totalApprovals})</h4>
            <div class="sa-pills">
                <span class="stat-pill approved sm"><span class="stat-pill-value">${d.approvedCount}</span><span class="stat-pill-label">Onay</span></span>
                <span class="stat-pill rejected sm"><span class="stat-pill-value">${d.rejectedCount}</span><span class="stat-pill-label">Red</span></span>
                <span class="stat-pill expired sm"><span class="stat-pill-value">${d.expiredCount}</span><span class="stat-pill-label">Timeout</span></span>
            </div>
            ${items}
        </div>`;
    }

    _renderSessionEscalations(d) {
        const items = d.escalationDetails.map(e => `
            <div class="sa-detail-item">
                <span>${this.escapeHtml(e.reason)}</span>
                <span class="status-tag ${this.statusClassName(e.status)}">${e.status}</span>
                ${e.agentName ? `<code>${this.escapeHtml(e.agentName)}</code>` : ''}
                ${e.resolution ? `<span class="muted">→ ${this.escapeHtml(e.resolution)}</span>` : ''}
            </div>`).join('');
        return `<div class="sa-section">
            <h4>🚨 Eskalasyonlar (${d.totalEscalations})</h4>
            ${items}
        </div>`;
    }

    renderRatingDistribution(dist, total) {
        const el = this.$('#ratingDistribution');
        if (!el) return;
        if (!total) {
            el.innerHTML = '<div class="empty-state">Henüz değerlendirme yok.</div>';
            return;
        }
        const labels = { 1: 'Çok Kötü', 2: 'Kötü', 3: 'Orta', 4: 'İyi', 5: 'Mükemmel' };
        const colors = { 1: '#ef4444', 2: '#f97316', 3: '#eab308', 4: '#22c55e', 5: '#10b981' };
        let html = '';
        for (let star = 5; star >= 1; star--) {
            const count = (dist && dist[star]) || 0;
            const pct = total > 0 ? Math.round((count / total) * 100) : 0;
            html += `
                <div class="rating-bar-row">
                    <span class="rating-bar-label">${star} ★ <small>${labels[star]}</small></span>
                    <div class="rating-bar-track">
                        <div class="rating-bar-fill" style="width:${pct}%;background:${colors[star]}"></div>
                    </div>
                    <span class="rating-bar-count">${count}</span>
                </div>`;
        }
        el.innerHTML = html;
    }

    renderApprovalStats(d) {
        const el = this.$('#approvalStats');
        if (!el) return;
        if (!d.totalApprovals) {
            el.innerHTML = '<div class="empty-state">Henüz onay işlemi yok.</div>';
            return;
        }
        el.innerHTML = `
            <div class="stat-pills">
                <div class="stat-pill approved">
                    <span class="stat-pill-value">${d.approvedCount}</span>
                    <span class="stat-pill-label">Onaylandı</span>
                </div>
                <div class="stat-pill rejected">
                    <span class="stat-pill-value">${d.rejectedCount}</span>
                    <span class="stat-pill-label">Reddedildi</span>
                </div>
                <div class="stat-pill expired">
                    <span class="stat-pill-value">${d.expiredCount}</span>
                    <span class="stat-pill-label">Timeout</span>
                </div>
                <div class="stat-pill pending">
                    <span class="stat-pill-value">${d.pendingCount}</span>
                    <span class="stat-pill-label">Beklemede</span>
                </div>
            </div>
            <div class="stat-total">Toplam: ${d.totalApprovals}</div>`;
    }

    renderEscalationStats(d) {
        const el = this.$('#escalationStats');
        if (!el) return;
        if (!d.totalEscalations) {
            el.innerHTML = '<div class="empty-state">Henüz eskalasyon yok.</div>';
            return;
        }
        el.innerHTML = `
            <div class="stat-pills">
                <div class="stat-pill open">
                    <span class="stat-pill-value">${d.openEscalations}</span>
                    <span class="stat-pill-label">Açık</span>
                </div>
                <div class="stat-pill acknowledged">
                    <span class="stat-pill-value">${d.acknowledgedEscalations}</span>
                    <span class="stat-pill-label">Üstlenildi</span>
                </div>
                <div class="stat-pill resolved">
                    <span class="stat-pill-value">${d.resolvedEscalations}</span>
                    <span class="stat-pill-label">Çözüldü</span>
                </div>
                <div class="stat-pill dismissed">
                    <span class="stat-pill-value">${d.dismissedEscalations}</span>
                    <span class="stat-pill-label">Dismiss</span>
                </div>
            </div>
            <div class="stat-total">Toplam: ${d.totalEscalations}</div>`;
    }

    renderDistributionList(selector, data, labelFn) {
        const el = this.$(selector);
        if (!el) return;
        const entries = data ? Object.entries(data) : [];
        if (entries.length === 0) {
            el.innerHTML = '<div class="empty-state">Veri yok.</div>';
            return;
        }
        const total = entries.reduce((s, [, v]) => s + v, 0);
        const sorted = entries.sort((a, b) => b[1] - a[1]);
        const colors = ['#6366f1', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981', '#3b82f6', '#ef4444', '#14b8a6'];
        let html = '';
        sorted.forEach(([key, count], i) => {
            const pct = total > 0 ? Math.round((count / total) * 100) : 0;
            const color = colors[i % colors.length];
            const label = labelFn ? labelFn(key) : key;
            html += `
                <div class="dist-row">
                    <span class="dist-label">${this.escapeHtml(label)}</span>
                    <div class="dist-bar-track">
                        <div class="dist-bar-fill" style="width:${pct}%;background:${color}"></div>
                    </div>
                    <span class="dist-count">${count} (${pct}%)</span>
                </div>`;
        });
        el.innerHTML = html;
    }

    intentLabel(key) {
        const map = {
            'sipariş_oluşturma': 'Sipariş Oluşturma',
            'sipariş_sorgulama': 'Sipariş Sorgulama',
            'sipariş_listeleme': 'Sipariş Listeleme',
            'şikayet': 'Şikayet',
            'ürün_bilgisi': 'Ürün Bilgisi',
            'genel': 'Genel',
            'bilinmiyor': 'Bilinmiyor'
        };
        return map[key] || key;
    }

    phaseLabel(key) {
        const map = {
            'greeting': 'Karşılama',
            'inquiry': 'Sorgulama',
            'action': 'Aksiyon',
            'resolution': 'Çözüm'
        };
        return map[key] || key;
    }

    sentimentLabel(key) {
        const map = {
            'positive': '😊 Olumlu',
            'neutral': '😐 Nötr',
            'negative': '😟 Olumsuz',
            'angry': '😡 Kızgın'
        };
        return map[key] || key;
    }

    renderRecentRatings(ratings) {
        const el = this.$('#recentRatingsList');
        if (!el) return;
        if (!ratings || ratings.length === 0) {
            el.innerHTML = '<div class="empty-state">Henüz değerlendirme yok.</div>';
            return;
        }
        const labels = { 1: 'Çok Kötü', 2: 'Kötü', 3: 'Orta', 4: 'İyi', 5: 'Mükemmel' };
        let html = '';
        ratings.forEach(r => {
            const stars = '★'.repeat(r.stars) + '☆'.repeat(5 - r.stars);
            html += `
                <div class="recent-rating-item">
                    <div class="recent-rating-header">
                        <span class="recent-rating-stars">${stars}</span>
                        <span class="recent-rating-label">${labels[r.stars] || ''}</span>
                        <time class="muted">${this.formatTime(r.ratedAt)}</time>
                    </div>
                    <div class="recent-rating-session">
                        <code>${this.escapeHtml((r.sessionId || '').slice(0, 12))}…</code>
                    </div>
                    ${r.feedback ? `<div class="recent-rating-feedback">${this.escapeHtml(r.feedback)}</div>` : ''}
                </div>`;
        });
        el.innerHTML = html;
    }

    async loadAll() {
        // Önce aktif sohbetleri çek (takenOverSessionIds set'ini doldurur),
        // sonra diğer yüklemeleri paralel yap — böylece renderEscalationCard
        // hangi sohbetin devralındığını bilir.
        await this.loadActiveChats();
        await Promise.all([
            this.loadApprovals(),
            this.loadEscalations(),
            this.loadAnalytics()
        ]);
    }

    // ─── Auto-refresh ───
    startRefresh() {
        this.stopRefresh();
        this.refreshTimer = setInterval(() => this.loadAll(), AdminPanel.REFRESH_INTERVAL_MS);
    }

    stopRefresh() {
        if (this.refreshTimer) {
            clearInterval(this.refreshTimer);
            this.refreshTimer = null;
        }
    }
}

// ─── UYGULAMA BAŞLATMA ───
function _initAdminPanel() {
    const admin = new AdminPanel();
    admin.init();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', _initAdminPanel);
} else {
    _initAdminPanel();
}
