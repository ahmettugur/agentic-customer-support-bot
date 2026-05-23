// admin-chat-bridge.js
// Called as: __adminChatSetup(token, apiBase)

window.__adminChatSetup = function (token, apiBase) {
    window.__adminToken = token;
    window.__adminApiBase = (apiBase || '').replace(/\/+$/, '');
};

// requestAnimationFrame ile bir sonraki paint'i bekle → scrollHeight yeni DOM'u yansıtır
window.__adminScrollToBottom = function (id) {
    var el = document.getElementById(id);
    if (!el) return;
    requestAnimationFrame(function () { el.scrollTop = el.scrollHeight; });
};

window.__adminSubscribeChat = function (ref, sessionId) {
    if (window.__adminChatEs) { window.__adminChatEs.close(); window.__adminChatEs = null; }
    var url = window.__adminApiBase + '/chat-sessions/' + encodeURIComponent(sessionId) +
        '/subscribe?access_token=' + encodeURIComponent(window.__adminToken);
    var es = new EventSource(url);
    window.__adminChatEs = es;
    es.addEventListener('bridge_message', function (e) {
        ref.invokeMethodAsync('OnChatEvent', 'bridge_message', e.data || '{}').catch(function () { });
    });
    es.onerror = function () { };
};

window.__adminStopChat = function () {
    if (window.__adminChatEs) { window.__adminChatEs.close(); window.__adminChatEs = null; }
};
