// chat-bridge.js
// Called as: __chatSetup(dotNetRef, apiBaseUrl)
// apiBaseUrl example: "https://localhost:7095"

// requestAnimationFrame ile bir sonraki paint'i bekle → scrollHeight yeni DOM'u yansıtır
window.__scrollToBottom = function (id) {
    var el = document.getElementById(id);
    if (!el) return;
    requestAnimationFrame(function () { el.scrollTop = el.scrollHeight; });
};

window.__chatSetup = function (ref, apiBase, authToken) {
    apiBase = (apiBase || '').replace(/\/+$/, '');
    window._blazorChatRef = ref;
    window._authToken = authToken || null;

    // Ajan-isim haritasının tek doğruluk kaynağı C# tarafı (Chat.razor._agentNameMap) —
    // burada elle kopya tutmak yerine bir kez JSInterop ile çekip cache'liyoruz.
    // Assembly adı: CustomerSupportBot.Web (RootNamespace override yok, proje adıyla aynı).
    DotNet.invokeMethodAsync('CustomerSupportBot.Web', 'GetAgentNameMap')
        .then(function (map) { window._agentNameMap = map || {}; })
        .catch(function () { window._agentNameMap = {}; });

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
        // Native sesli modun (realtime-native) balonları. #messages Blazor'un render
        // ağacıdır: eskiden buraya ham DOM düğümleri ekleniyordu — Blazor onları bilmediği için
        // "yeni sohbet"te silinmiyor, sonraki render'larda Blazor'un kendi düğümleriyle sırası
        // karışıyordu. Artık JS yalnızca bir tutamaç (id) üretir; içerik Chat.razor'daki
        // _messages listesine yazılır ve Blazor çizer. Çağrılar sırayla .NET'e gider.
        ui: {
            _seq: 0,
            _newHandle: function () { return { id: 'voice-' + Date.now() + '-' + (++this._seq) }; },
            _call: function (method) {
                var args = Array.prototype.slice.call(arguments, 1);
                ref.invokeMethodAsync.apply(ref, [method].concat(args)).catch(function () { });
            },
            // opts: { placeholder: bool, before: handle }
            addMessage: function (role, text, opts) {
                var h = this._newHandle();
                opts = opts || {};
                this._call('VoiceNativeAdd', h.id, role, text || '', !!opts.placeholder,
                    opts.before ? opts.before.id : null);
                return h;
            },
            setMessageText: function (h, text, placeholder) {
                if (h) this._call('VoiceNativeSetText', h.id, text || '', !!placeholder);
            },
            removeMessage: function (h) {
                if (h) this._call('VoiceNativeRemove', h.id);
            },
            startStreamingMessage: function () {
                var h = this._newHandle();
                this._call('VoiceNativeAdd', h.id, 'assistant', '', false, null);
                return h;
            },
            finalizeStreamingMessage: function (h) {
                if (h) this._call('VoiceNativeFinalize', h.id);
            },
            appendResponseChunk: function (h, text) {
                if (h && text) this._call('VoiceNativeAppend', h.id, text);
            },
            setAgentStatus: function (h, label, state) {
                if (h) this._call('VoiceNativeStatus', h.id, label + (state === 'running' ? '…' : ' ✓'));
            },
            scrollToBottom: function () { window.__scrollToBottom('messages'); }
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
    // Token'ı güncellemek için tek giriş noktası — AppAuthStateProvider/AuthService'in
    // arka planda yenilediği token'ı buradan JS tarafına taşımak için kullanılır.
    window.__setAuthToken = function (token) {
        window._authToken = token || null;
    };

    // isRetry=true ise 401 alındığında YENİDEN refresh denenmez — doğrudan OnStreamError'a
    // düşer. Bu, refresh edilmiş ama yine de geçersiz olan bir token'ın (ör. sunucu tarafında
    // ayrıca reddedilmesi) sonsuz refresh döngüsüne girmesini engeller; her mesaj için en
    // fazla bir kez otomatik retry yapılır.
    // attachmentIds: POST /chat/attachments ile yüklenmiş fotoğrafların kimlikleri (boş olabilir).
    window.__streamChat = function (ref, apiBase, query, sessionId, isRetry, attachmentIds) {
        var ids = Array.isArray(attachmentIds) && attachmentIds.length > 0 ? attachmentIds : null;
        var ctrl = new AbortController();
        window._chatStreamAbort = ctrl;
        (async function () {
            try {
                var url = apiBase + '/chat/stream';
                var headers = { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' };
                if (window._authToken) headers['Authorization'] = 'Bearer ' + window._authToken;
                var r = await fetch(url, {
                    method: 'POST',
                    headers: headers,
                    body: JSON.stringify({ query: query, sessionId: sessionId || null, attachmentIds: ids }),
                    signal: ctrl.signal
                });
                if (!r.ok) {
                    if (r.status === 401 && !isRetry) {
                        // C# tarafı refresh dener; başarılıysa __streamChat'i isRetry=true ile
                        // tekrar çağırır, başarısızsa login'e yönlendirir. Bu fetch burada biter.
                        ref.invokeMethodAsync('OnStreamUnauthorized', query, sessionId || null, ids).catch(function () { });
                        return;
                    }
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
    // isAuthRetry=true, C#'ın OnPersistentEventsError sonrası yeniden bağlanmak için yaptığı
    // çağrıdır — bu durumda _persistentEventsRetried SIFIRLANMAZ. Sıfırlanırsa ve yeni token
    // da (ör. refresh token da geçersizse ya da farklı bir sebeple) reddedilirse, ikinci hata
    // tekrar C#'a bildirilip tekrar refresh denenir — sonsuz bir JS↔C# döngüsü doğar. Normal
    // (yeni oturum bağlama) çağrılarda ikinci parametre verilmez, flag her zaman sıfırlanır.
    window._startPersistentEvents = function (sid, isAuthRetry) {
        if (window._chatEs) window._chatEs.close();
        if (!isAuthRetry) window._persistentEventsRetried = false;
        // EventSource header desteklemez — token'ı query string ile taşıyoruz
        // (bkz. AuthServicesExtensions.cs OnMessageReceived, access_token'ı bearer olarak okuyor).
        var tokenQs = window._authToken ? '?access_token=' + encodeURIComponent(window._authToken) : '';
        var es = new EventSource(apiBase + '/chat/events/' + encodeURIComponent(sid) + tokenQs);
        window._chatEs = es;
        ['human_joined', 'human_left', 'bot_typing', 'human_message', 'handoff_pending', 'handoff_cleared', 'approval_resolved'].forEach(function (t) {
            es.addEventListener(t, function (e) {
                ref.invokeMethodAsync('OnPersistentEvent', t, e.data || '{}');
            });
        });
        // EventSource, hata sebebini (401 dahil) tarayıcı API'sinde expose etmez — ama
        // token'ın süresi dolmuşsa bağlantı asla kurulamaz ve tarayıcı AYNI (artık geçersiz)
        // URL'yle sonsuza dek kendi kendine yeniden bağlanmayı dener; kullanıcı hiçbir uyarı
        // görmeden bildirim kanalı kalıcı olarak ölü kalır. Bu yüzden ilk hatada BİR kez
        // C#'a haber verip token'ı yeniletiyoruz; o da başarısız olursa (_persistentEventsRetried
        // zaten true olduğu için) burada tekrar denenmez — sonraki native reconnect
        // denemeleri sessizce devam eder, kullanıcıyı spam'lemeyiz.
        es.onerror = function () {
            if (window._persistentEventsRetried) return;
            window._persistentEventsRetried = true;
            ref.invokeMethodAsync('OnPersistentEventsError', sid).catch(function () { });
        };
    };

    window._stopPersistentEvents = function () {
        if (window._chatEs) { window._chatEs.close(); window._chatEs = null; }
    };
};
