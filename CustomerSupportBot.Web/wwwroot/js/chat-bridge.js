// chat-bridge.js
// Called as: __chatSetup(dotNetRef, apiBaseUrl)
// apiBaseUrl example: "https://localhost:7095"

// requestAnimationFrame ile bir sonraki paint'i bekle → scrollHeight yeni DOM'u yansıtır
window.__scrollToBottom = function (id) {
    var el = document.getElementById(id);
    if (!el) return;
    requestAnimationFrame(function () { el.scrollTop = el.scrollHeight; });
};

window.__chatSetup = function (ref, apiBase) {
    apiBase = (apiBase || '').replace(/\/+$/, '');
    window._blazorChatRef = ref;

    // Ajan-isim haritasının tek doğruluk kaynağı C# tarafı (Chat.razor._agentNameMap) —
    // burada elle kopya tutmak yerine bir kez JSInterop ile çekip cache'liyoruz.
    // Assembly adı: CustomerSupportBot.Web (RootNamespace override yok, proje adıyla aynı).
    DotNet.invokeMethodAsync('CustomerSupportBot.Web', 'GetAgentNameMap')
        .then(function (map) { window._agentNameMap = map || {}; })
        .catch(function () { window._agentNameMap = {}; });

    // ── Minimal window.App bridge (realtime-ui.js uses this) ─────────────────
    window.App = {
        sendMessage: function (t) { ref.invokeMethodAsync('VoiceSendMessage', t); },
        newChat: function () { ref.invokeMethodAsync('NewChatFromVoice'); },
        voiceTranscript: function (t) { ref.invokeMethodAsync('VoiceTranscript', t).catch(function () { }); },
        voiceStreamEvent: function (type, data) { ref.invokeMethodAsync('OnStreamEvent', type, JSON.stringify(data || {})).catch(function () { }); },
        voiceStreamComplete: function () { ref.invokeMethodAsync('OnStreamComplete').catch(function () { }); }
    };

    // ── window.chatApp — full shim for realtime-ui.js ─────────────────────────
    window.chatApp = {
        api: {
            baseUrl: apiBase,
            sessionId: null,
            setSession: function (sid) {
                this.sessionId = sid;
                ref.invokeMethodAsync('VoiceSetSession', sid);
            },
            resetSession: function () { this.sessionId = null; }
        },
        ui: {
            addMessage: function (role, text) {
                var msgs = document.getElementById('messages');
                if (!msgs) return null;
                var welcome = document.getElementById('welcome');
                if (welcome) welcome.style.display = 'none';
                var div = document.createElement('div');
                div.className = 'message ' + role;
                var bubble = document.createElement('div');
                bubble.className = 'bubble message-bubble';
                bubble.textContent = text || '';
                div.appendChild(bubble);
                msgs.appendChild(div);
                msgs.scrollTop = msgs.scrollHeight;
                return div;
            },
            startStreamingMessage: function () {
                var msgs = document.getElementById('messages');
                if (!msgs) return null;
                var div = document.createElement('div');
                div.className = 'message assistant';
                var bubble = document.createElement('div');
                bubble.className = 'bubble message-bubble';
                div.appendChild(bubble);
                msgs.appendChild(div);
                msgs.scrollTop = msgs.scrollHeight;
                return { messageDiv: div, bubble: bubble, _text: '' };
            },
            finalizeStreamingMessage: function (ctx) { /* no-op */ },
            appendResponseChunk: function (ctx, text) {
                if (!ctx || !ctx.bubble || !text) return;
                ctx._text = (ctx._text || '') + text;
                ctx.bubble.textContent = ctx._text;
                var msgs = document.getElementById('messages');
                if (msgs) msgs.scrollTop = msgs.scrollHeight;
            },
            setAgentStatus: function (ctx, label, state) {
                if (!ctx || !ctx.bubble) return;
                var chip = ctx.bubble.querySelector('.voice-agent-chip');
                if (!chip) {
                    chip = document.createElement('div');
                    chip.className = 'voice-agent-chip';
                    chip.style.cssText = 'font-size:11px;opacity:.6;margin-bottom:6px;color:inherit';
                    ctx.bubble.insertBefore(chip, ctx.bubble.firstChild);
                }
                chip.textContent = label + (state === 'running' ? '…' : ' ✓');
            },
            scrollToBottom: function () {
                var msgs = document.getElementById('messages');
                if (msgs) msgs.scrollTop = msgs.scrollHeight;
            }
        },
        _handleStreamEvent: function (ctx, reasoningState, evt) {
            if (!ctx) return;
            var d = evt.data || {};
            switch (evt.type) {
                case 'reasoning_start':
                    reasoningState.buffer = '';
                    if (!ctx._reasoningPanel) {
                        var content = ctx.messageDiv.querySelector('.message-content');
                        if (!content) {
                            content = document.createElement('div');
                            content.className = 'message-content';
                            ctx.messageDiv.replaceChild(content, ctx.bubble);
                            content.appendChild(ctx.bubble);
                        }
                        var panel = document.createElement('details');
                        panel.className = 'reasoning-panel streaming';
                        panel.open = true;
                        panel.innerHTML =
                            '<summary class="reasoning-summary">' +
                            '<span class="reasoning-icon">🧠</span>' +
                            '<span class="reasoning-label">Düşünce süreci</span>' +
                            '<span class="reasoning-dots inline"><span></span><span></span><span></span></span>' +
                            '<span class="reasoning-chevron"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg></span>' +
                            '</summary>' +
                            '<div class="reasoning-body"><div class="reasoning-section-text"></div></div>';
                        content.insertBefore(panel, ctx.bubble);
                        ctx._reasoningPanel = panel;
                    }
                    break;
                case 'reasoning_delta':
                    if (ctx._reasoningPanel && d.text) {
                        reasoningState.buffer += d.text;
                        var textEl = ctx._reasoningPanel.querySelector('.reasoning-section-text');
                        if (textEl) textEl.textContent = this._extractAnalysis(reasoningState.buffer);
                    }
                    break;
                case 'reasoning_complete':
                    if (ctx._reasoningPanel) {
                        ctx._reasoningPanel.classList.remove('streaming');
                        var sum = ctx._reasoningPanel.querySelector('.reasoning-summary');
                        var dots = sum && sum.querySelector('.reasoning-dots');
                        if (dots) dots.remove();
                        if (d.confidence) {
                            var badge = document.createElement('span');
                            badge.className = 'reasoning-badge';
                            badge.textContent = d.confidence;
                            var chev = sum && sum.querySelector('.reasoning-chevron');
                            if (chev) sum.insertBefore(badge, chev);
                        }
                        if (d.analysis) {
                            var body = ctx._reasoningPanel.querySelector('.reasoning-body');
                            if (body) {
                                body.innerHTML = '<div class="reasoning-section"><div class="reasoning-section-text">' +
                                    this._htmlEsc(d.analysis) + '</div></div>';
                            }
                        }
                    }
                    break;
                case 'agent':
                    if (d.name && this.ui && this.ui.setAgentStatus) {
                        var label = this._friendlyAgent(d.name);
                        var agentState = (d.status === 'done') ? 'completed' : 'running';
                        try { this.ui.setAgentStatus(ctx, label, agentState); } catch (e) { }
                    }
                    break;
                case 'response_start':
                    break;
                case 'response_delta':
                    if (d.text) this.ui.appendResponseChunk(ctx, d.text);
                    break;
                case 'response_complete':
                    if (d.text) { if (ctx.bubble) ctx.bubble.textContent = d.text; ctx._text = d.text; }
                    break;
            }
        },
        _extractAnalysis: function (raw) {
            var marker = '"analysis"';
            var idx = raw.indexOf(marker);
            if (idx < 0) return '';
            var ci = raw.indexOf(':', idx + marker.length);
            if (ci < 0) return '';
            var qs = raw.indexOf('"', ci + 1);
            if (qs < 0) return '';
            var vs = qs + 1, qe = -1;
            for (var i = vs; i < raw.length; i++) {
                if (raw[i] === '\\') { i++; continue; }
                if (raw[i] === '"') { qe = i; break; }
            }
            return qe > vs ? raw.substring(vs, qe) : raw.substring(vs);
        },
        _htmlEsc: function (s) {
            return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        },
        _friendlyAgent: function (id) {
            // Workflow executor id'leri "<AjanAdı>_<guid>" biçiminde gelir — eşleşme
            // için guid soneki atılır. Harita artık C#'tan (Chat.razor._agentNameMap)
            // JSInterop ile çekiliyor (bkz. __chatSetup) — tek doğruluk kaynağı, elle
            // senkron tutulan ikinci bir kopya yok.
            var baseName = id.indexOf('_') >= 0 ? id.substring(0, id.indexOf('_')) : id;
            var map = window._agentNameMap || {};
            return map[baseName] || (baseName.indexOf('SubTask#') === 0 ? 'Alt Görev ' + baseName.slice(8) : baseName);
        }
    };

    // ── SSE Streaming via native fetch ────────────────────────────────────────
    // Regular (non-async) function so JS.InvokeVoidAsync returns immediately.
    // The actual fetch+stream runs inside an IIFE so C# is never blocked waiting
    // for the Promise — OnStreamEvent callbacks arrive in real time.
    window.__streamChat = function (ref, apiBase, query, sessionId) {
        var ctrl = new AbortController();
        window._chatStreamAbort = ctrl;
        (async function () {
            try {
                var url = apiBase + '/chat/stream';
                var r = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
                    body: JSON.stringify({ query: query, sessionId: sessionId || null }),
                    signal: ctrl.signal
                });
                if (!r.ok) {
                    ref.invokeMethodAsync('OnStreamError', 'HTTP ' + r.status).catch(function () { });
                    return;
                }
                var reader = r.body.getReader();
                var dec = new TextDecoder();
                var buf = '', evType = 'message', dlines = [];
                while (true) {
                    var chunk = await reader.read();
                    if (chunk.done) break;
                    buf += dec.decode(chunk.value, { stream: true });
                    var parts = buf.split('\n');
                    buf = parts.pop();
                    for (var i = 0; i < parts.length; i++) {
                        var ln = parts[i];
                        if (ln.startsWith('event:')) { evType = ln.slice(6).trim(); }
                        else if (ln.startsWith('data:')) { dlines.push(ln.slice(5).trim()); }
                        else if (ln.length === 0 && dlines.length > 0) {
                            ref.invokeMethodAsync('OnStreamEvent', evType, dlines.join('\n')).catch(function (e) { console.error('[streamChat] invoke err:', e); });
                            evType = 'message'; dlines = [];
                        }
                    }
                }
                ref.invokeMethodAsync('OnStreamComplete').catch(function () { });
            } catch (e) {
                console.error('[streamChat] ERROR:', e.name, e.message);
                if (e.name === 'AbortError') {
                    ref.invokeMethodAsync('OnStreamComplete').catch(function () { });
                } else {
                    ref.invokeMethodAsync('OnStreamError', e.message || String(e)).catch(function () { });
                }
            }
            window._chatStreamAbort = null;
        })();
    };

    window.__stopStream = function () {
        if (window._chatStreamAbort) { window._chatStreamAbort.abort(); window._chatStreamAbort = null; }
    };

    // ── Persistent EventSource (uses absolute API URL) ────────────────────────
    window._startPersistentEvents = function (sid) {
        if (window._chatEs) window._chatEs.close();
        var es = new EventSource(apiBase + '/chat/events/' + encodeURIComponent(sid));
        window._chatEs = es;
        ['human_joined', 'human_left', 'bot_typing', 'human_message', 'handoff_pending', 'handoff_cleared'].forEach(function (t) {
            es.addEventListener(t, function (e) {
                ref.invokeMethodAsync('OnPersistentEvent', t, e.data || '{}');
            });
        });
        es.onerror = function () { };
    };

    window._stopPersistentEvents = function () {
        if (window._chatEs) { window._chatEs.close(); window._chatEs = null; }
    };
};
