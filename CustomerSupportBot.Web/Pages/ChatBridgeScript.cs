namespace CustomerSupportBot.Web.Pages;

internal static class ChatBridgeScript
{
    // Called as: __chatSetup(dotNetRef, apiBaseUrl)
    // apiBaseUrl example: "http://localhost:5021"
    internal const string Setup = @"window.__chatSetup = function(ref, apiBase) {
    apiBase = (apiBase || '').replace(/\/+$/, '');
    window._blazorChatRef = ref;

    // ── Minimal window.App bridge (realtime-ui.js uses this) ──────────────────
    window.App = {
        sendMessage: function(t) { ref.invokeMethodAsync('VoiceSendMessage', t); },
        newChat:     function()  { ref.invokeMethodAsync('NewChatFromVoice'); }
    };

    // ── window.chatApp — full shim for realtime-ui.js ─────────────────────────
    window.chatApp = {
        api: {
            baseUrl: apiBase,
            sessionId: null,
            setSession: function(sid) {
                this.sessionId = sid;
                ref.invokeMethodAsync('VoiceSetSession', sid);
            },
            resetSession: function() { this.sessionId = null; }
        },
        ui: {
            addMessage: function(role, text) {
                var msgs = document.getElementById('messages');
                if (!msgs) return null;
                // Hide the Blazor-rendered welcome screen when voice adds first message
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
            startStreamingMessage: function() {
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
            finalizeStreamingMessage: function(ctx) { /* no-op */ },
            appendResponseChunk: function(ctx, text) {
                if (!ctx || !ctx.bubble || !text) return;
                ctx._text = (ctx._text || '') + text;
                ctx.bubble.textContent = ctx._text;
                var msgs = document.getElementById('messages');
                if (msgs) msgs.scrollTop = msgs.scrollHeight;
            },
            setAgentStatus: function(ctx, label, state) {
                if (!ctx || !ctx.bubble) return;
                // Insert chip INSIDE the bubble (not as a sibling) to avoid flex layout issues
                var chip = ctx.bubble.querySelector('.voice-agent-chip');
                if (!chip) {
                    chip = document.createElement('div');
                    chip.className = 'voice-agent-chip';
                    chip.style.cssText = 'font-size:11px;opacity:.6;margin-bottom:6px;color:inherit';
                    ctx.bubble.insertBefore(chip, ctx.bubble.firstChild);
                }
                chip.textContent = label + (state === 'running' ? '…' : ' ✓');
            },
            scrollToBottom: function() {
                var msgs = document.getElementById('messages');
                if (msgs) msgs.scrollTop = msgs.scrollHeight;
            }
        },
        _handleStreamEvent: function(ctx, reasoningState, evt) {
            if (!ctx) return;
            if (evt.type === 'response_delta' && evt.data && evt.data.delta)
                this.ui.appendResponseChunk(ctx, evt.data.delta);
            else if (evt.type === 'response_complete' && evt.data && evt.data.content) {
                if (ctx.bubble) ctx.bubble.textContent = evt.data.content;
                ctx._text = evt.data.content;
            }
        }
    };

    // ── SSE Streaming via native fetch (works with HTTP/1.1, no ALPN needed) ────
    // Regular (non-async) function so JS.InvokeVoidAsync returns immediately.
    // The actual fetch+stream runs inside an IIFE so C# is never blocked waiting
    // for the Promise — OnStreamEvent callbacks arrive in real time.
    window.__streamChat = function(ref, apiBase, query, sessionId) {
        console.log('[streamChat] START apiBase=' + apiBase + ' query=' + query + ' sid=' + sessionId);
        var ctrl = new AbortController();
        window._chatStreamAbort = ctrl;
        (async function() {
            try {
                var url = apiBase + '/chat/stream';
                console.log('[streamChat] fetch ' + url);
                var r = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
                    body: JSON.stringify({ query: query, sessionId: sessionId || null }),
                    signal: ctrl.signal
                });
                console.log('[streamChat] status=' + r.status);
                if (!r.ok) {
                    ref.invokeMethodAsync('OnStreamError', 'HTTP ' + r.status).catch(function(){});
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
                            console.log('[streamChat] event: ' + evType);
                            ref.invokeMethodAsync('OnStreamEvent', evType, dlines.join('\n')).catch(function(e){ console.error('[streamChat] invoke err:', e); });
                            evType = 'message'; dlines = [];
                        }
                    }
                }
                console.log('[streamChat] COMPLETE');
                ref.invokeMethodAsync('OnStreamComplete').catch(function(){});
            } catch(e) {
                console.error('[streamChat] ERROR:', e.name, e.message);
                if (e.name === 'AbortError') {
                    ref.invokeMethodAsync('OnStreamComplete').catch(function(){});
                } else {
                    ref.invokeMethodAsync('OnStreamError', e.message || String(e)).catch(function(){});
                }
            }
            window._chatStreamAbort = null;
        })();
    };
    window.__stopStream = function() {
        if (window._chatStreamAbort) { window._chatStreamAbort.abort(); window._chatStreamAbort = null; }
    };

    // ── Persistent EventSource (uses absolute API URL) ─────────────────────────
    window._startPersistentEvents = function(sid) {
        if (window._chatEs) window._chatEs.close();
        var es = new EventSource(apiBase + '/chat/events/' + encodeURIComponent(sid));
        window._chatEs = es;
        ['human_joined','human_left','bot_typing','human_message'].forEach(function(t) {
            es.addEventListener(t, function(e) {
                ref.invokeMethodAsync('OnPersistentEvent', t, e.data || '{}');
            });
        });
        es.onerror = function() {};
    };
    window._stopPersistentEvents = function() {
        if (window._chatEs) { window._chatEs.close(); window._chatEs = null; }
    };
};";
}
