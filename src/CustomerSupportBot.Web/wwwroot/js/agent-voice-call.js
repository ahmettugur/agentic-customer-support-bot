// agent-voice-call.js — temsilci ↔ müşteri sesli görüşmesi (WebRTC). Sinyal: POST …/signal; gelen sinyal SSE
// `voice_signal` olayıyla gelir. Müşteride sohbet akışı bu modüle iletir; temsilcide modül görüşmenin oturumuna
// kendi akışını açar — görüşme sohbet paneline bağlı değildir, temsilci başka sohbete geçince kesilmez.
// Kayıt YALNIZCA temsilci tarafında: yerel mikrofon ve uzak müşteri sesi ayrı ayrı, 10 sn'lik tam webm
// parçaları (kaydedici her parçada yeniden başlar) halinde yüklenir.
(function () {
    'use strict';
    var SEGMENT_MS = 10000;
    var CONNECT_TIMEOUT_MS = 20000;
    var DISCONNECT_GRACE_MS = 30000;

    var s = null; // aktif görüşme durumu

    // Kaydediciler durdurulunca son parça onstop'ta yüklenir; yükleme görüşme bağlamını kendisi taşıdığından
    // durum hemen temizlenebilir.
    function stopRecorders() {
        try { (s.recorders || []).forEach(function (r) { r.stopped = true; if (r.rec && r.rec.state !== 'inactive') r.rec.stop(); }); } catch (e) { }
    }

    function reset() {
        if (!s) return;
        stopRecorders();
        try { if (s.es) s.es.close(); } catch (e) { }
        try { if (s.pc) s.pc.close(); } catch (e) { }
        try { if (s.local) s.local.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { }
        if (s.audio) { s.audio.srcObject = null; s.audio.remove(); }
        clearTimeout(s.connectTimer); clearTimeout(s.disconnectTimer);
        s = null;
    }

    function notify(method) {
        if (!s || !s.ref) return;
        var args = Array.prototype.slice.call(arguments);
        s.ref.invokeMethodAsync.apply(s.ref, args).catch(function () { });
    }

    function setState(state) { if (!s) return; s.state = state; if (state === 'connected') s.connectedAt = Date.now(); notify('OnVoiceState', state); }

    // Görüşme bitti: önce bildirim (ref temizlenmeden), sonra durum. Temsilci çubuğu bitiş türünü ister
    // (ret/cevapsız bildirimi), müşteri kartı yalnızca durumu.
    function finish(type, reason) {
        if (s && s.role === 'staff') notify('OnVoiceEnded', type, reason || null);
        else notify('OnVoiceState', 'ended');
        reset();
    }

    function post(path, body, contentType) {
        var headers = { 'Authorization': 'Bearer ' + s.token };
        if (contentType) headers['Content-Type'] = contentType;
        return fetch(s.apiBase + path, { method: 'POST', headers: headers, body: body });
    }

    function sendSignal(type, data) {
        if (!s) return;
        post(s.signalPath, JSON.stringify({ callId: s.callId, type: type, data: data }), 'application/json').catch(function () { });
    }

    async function iceServers() {
        var r = await fetch(s.apiBase + s.icePath, { headers: { 'Authorization': 'Bearer ' + s.token } });
        if (!r.ok) return [];
        return (await r.json()).iceServers || [];
    }

    async function getMic() {
        return navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    }

    async function createPeer() {
        var pc = new RTCPeerConnection({ iceServers: await iceServers() });
        s.pc = pc;
        s.pendingIce = [];
        s.local.getTracks().forEach(function (t) { pc.addTrack(t, s.local); });
        pc.onicecandidate = function (e) { if (e.candidate) sendSignal('ice', e.candidate.toJSON()); };
        pc.ontrack = function (e) {
            s.remote = e.streams[0];
            if (!s.audio) { s.audio = document.createElement('audio'); s.audio.autoplay = true; document.body.appendChild(s.audio); }
            s.audio.srcObject = s.remote;
            if (s.role === 'staff' && !s.recordingStarted) startRecording();
        };
        pc.onconnectionstatechange = function () {
            if (!s) return;
            var st = pc.connectionState;
            if (st === 'connected') {
                clearTimeout(s.connectTimer); clearTimeout(s.disconnectTimer);
                setState('connected');
            } else if (st === 'disconnected') {
                clearTimeout(s.disconnectTimer);
                s.disconnectTimer = setTimeout(function () { window.csbVoice.hangup('connection_lost'); }, DISCONNECT_GRACE_MS);
            } else if (st === 'failed') {
                window.csbVoice.hangup('connection_lost');
            }
        };
        s.connectTimer = setTimeout(function () {
            if (s && s.pc && s.pc.connectionState !== 'connected') window.csbVoice.hangup('connect_failed');
        }, CONNECT_TIMEOUT_MS);
        return pc;
    }

    async function addIce(candidate) {
        if (!s || !s.pc) return;
        if (!s.pc.remoteDescription) { s.pendingIce.push(candidate); return; }
        try { await s.pc.addIceCandidate(candidate); } catch (e) { }
    }

    async function flushIce() {
        var list = s.pendingIce || []; s.pendingIce = [];
        for (var i = 0; i < list.length; i++) { try { await s.pc.addIceCandidate(list[i]); } catch (e) { } }
    }

    // ── Kayıt (yalnız temsilci) ───────────────────────────────────────────────
    function startRecording() {
        s.recordingStarted = true;
        // Yüklemeler `s`'e değil bu bağlama dayanır: görüşme bitip durum temizlendikten sonra gelen son parça da yüklenir.
        var call = { apiBase: s.apiBase, token: s.token, callId: s.callId, start: performance.now() };
        s.recorders = [segmentRecorder(s.local, 'agent', call), segmentRecorder(s.remote, 'customer', call)];
        notify('OnVoiceRecording', true);
    }

    function segmentRecorder(stream, track, call) {
        var mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus' : 'audio/ogg;codecs=opus';
        var state = { seq: 0, stopped: false, rec: null };
        function next() {
            if (state.stopped) return;
            var startedAt = performance.now();
            var rec = new MediaRecorder(stream, { mimeType: mime, audioBitsPerSecond: 32000 });
            state.rec = rec;
            var parts = [];
            rec.ondataavailable = function (e) { if (e.data && e.data.size) parts.push(e.data); };
            rec.onstop = function () {
                var blob = new Blob(parts, { type: mime.split(';')[0] });
                var offset = Math.max(0, Math.round(startedAt - call.start));
                var duration = Math.round(performance.now() - startedAt);
                upload(call, track, state.seq++, offset, duration, blob);
                next();
            };
            rec.start();
            setTimeout(function () { if (rec.state !== 'inactive') rec.stop(); }, SEGMENT_MS);
        }
        next();
        return state;
    }

    async function upload(call, track, seq, offsetMs, durationMs, blob, attempt) {
        if (!blob.size) return;
        attempt = attempt || 0;
        var url = call.apiBase + '/voice-calls/' + encodeURIComponent(call.callId) + '/chunks?track=' + track + '&seq=' + seq
            + '&offsetMs=' + offsetMs + '&durationMs=' + durationMs;
        try {
            var r = await fetch(url, { method: 'POST', headers: { 'Authorization': 'Bearer ' + call.token, 'Content-Type': blob.type }, body: blob });
            if (r.ok || r.status === 409 || r.status === 403) return;
            throw new Error('HTTP ' + r.status);
        } catch (e) {
            if (attempt < 3) setTimeout(function () { upload(call, track, seq, offsetMs, durationMs, blob, attempt + 1); }, 1000 * (attempt + 1));
            else notify('OnVoiceError', 'Kayıt kesintiye uğradı.');
        }
    }

    // Temsilci: görüşmenin oturumundaki sinyalleri dinleyen akış (yalnızca voice_signal kullanılır).
    function openStaffStream() {
        var url = s.apiBase + '/agent/chat-sessions/' + encodeURIComponent(s.sessionId) + '/subscribe?access_token=' + encodeURIComponent(s.token);
        var es = new EventSource(url);
        es.addEventListener('voice_signal', function (e) { window.csbVoice.staffOnSignal(e.data || '{}'); });
        es.onerror = function () { };
        s.es = es;
    }

    window.csbVoice = {
        isActive: function () { return !!s; },

        // Temsilci çubuğu yeniden açıldığında süren görüşmeye bağlanır; görüşme yoksa null.
        attach: function (callId, ref) {
            if (!s || s.role !== 'staff' || s.callId !== callId) return null;
            s.ref = ref;
            return { state: s.state || 'ringing', recording: !!s.recordingStarted, muted: !!s.muted, connectedAtMs: s.connectedAt || 0 };
        },

        // Çubuk kapandı: görüşme sürer, yalnızca bildirimler durur.
        detach: function (callId) { if (s && s.callId === callId) s.ref = null; },

        staffStart: function (o) {
            reset();
            s = { role: 'staff', apiBase: o.apiBase, token: o.token, callId: o.callId, sessionId: o.sessionId, ref: o.dotnetRef,
                  signalPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/signal',
                  hangupPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/hangup',
                  icePath: '/voice-calls/' + encodeURIComponent(o.callId) + '/ice-config' };
            openStaffStream();
            setState('ringing');
        },

        staffOnSignal: async function (json) {
            if (!s || s.role !== 'staff') return;
            var m = typeof json === 'string' ? JSON.parse(json) : json;
            if (m.callId !== s.callId) return;
            if (m.type === 'accepted') {
                try { s.local = await getMic(); }
                catch (e) { notify('OnVoiceError', 'Mikrofon izni verilmedi.'); window.csbVoice.hangup('agent_hangup'); return; }
                setState('connecting');
                var pc = await createPeer();
                var offer = await pc.createOffer();
                await pc.setLocalDescription(offer);
                sendSignal('offer', pc.localDescription.toJSON());
            } else if (m.type === 'answer') {
                await s.pc.setRemoteDescription(m.data); await flushIce();
            } else if (m.type === 'ice') {
                await addIce(m.data);
            } else if (m.type === 'declined' || m.type === 'ended') {
                finish(m.type, m.reason);
            }
        },

        customerAnswer: async function (o) {
            reset();
            s = { role: 'customer', apiBase: o.apiBase, token: o.token, callId: o.callId, ref: o.dotnetRef,
                  signalPath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/signal',
                  hangupPath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/hangup',
                  icePath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/ice-config' };
            try { s.local = await getMic(); }
            catch (e) {
                await post('/chat/voice-calls/' + encodeURIComponent(o.callId) + '/decline?reason=no_microphone');
                reset(); return false;
            }
            var r = await post('/chat/voice-calls/' + encodeURIComponent(o.callId) + '/accept');
            if (!r.ok) { reset(); return false; }
            setState('connecting');
            await createPeer();
            return true;
        },

        customerOnSignal: async function (json) {
            if (!s || s.role !== 'customer') return;
            var m = typeof json === 'string' ? JSON.parse(json) : json;
            if (m.callId !== s.callId) return;
            if (m.type === 'offer') {
                await s.pc.setRemoteDescription(m.data); await flushIce();
                var answer = await s.pc.createAnswer();
                await s.pc.setLocalDescription(answer);
                sendSignal('answer', s.pc.localDescription.toJSON());
            } else if (m.type === 'ice') {
                await addIce(m.data);
            } else if (m.type === 'ended') {
                finish('ended', m.reason);
            }
        },

        hangup: async function (reason) {
            if (!s) return;
            var mine = s;
            var path = s.hangupPath + '?reason=' + encodeURIComponent(reason || '');
            stopRecorders();
            try { await post(path); } catch (e) { }
            if (s === mine) finish('ended', reason);
        },

        setMuted: function (muted) {
            if (!s) return;
            s.muted = !!muted;
            if (s.local) s.local.getAudioTracks().forEach(function (t) { t.enabled = !muted; });
        }
    };
})();

// İki izli kayıt oynatıcı: her iz kendi parça dizisini sırayla çalar, iki iz aynı anda başlar.
window.csbVoicePlayer = (function () {
    var p = null;
    function stop() {
        if (!p) return;
        p.players.forEach(function (x) { x.audio.pause(); x.urls.forEach(URL.revokeObjectURL); x.audio.remove(); });
        p = null;
    }
    async function blobUrl(apiBase, token, callId, chunkId) {
        var r = await fetch(apiBase + '/voice-calls/' + encodeURIComponent(callId) + '/chunks/' + encodeURIComponent(chunkId),
            { headers: { 'Authorization': 'Bearer ' + token } });
        if (!r.ok) return null;
        return URL.createObjectURL(await r.blob());
    }
    async function playTrack(o, chunks, startMs) {
        var audio = document.createElement('audio'); document.body.appendChild(audio);
        var player = { audio: audio, urls: [] };
        var i = chunks.findIndex(function (c) { return c.offsetMs + 10000 > startMs; });
        if (i < 0) return player;
        async function playAt(idx, seekMs) {
            if (!p || idx >= chunks.length) return;
            var url = await blobUrl(o.apiBase, o.token, o.callId, chunks[idx].chunkId);
            if (!url) return playAt(idx + 1, 0);
            player.urls.push(url);
            audio.src = url;
            audio.onloadedmetadata = function () { audio.currentTime = Math.max(0, seekMs / 1000); audio.play().catch(function () { }); };
            audio.onended = function () { playAt(idx + 1, 0); };
        }
        playAt(i, Math.max(0, startMs - chunks[i].offsetMs));
        return player;
    }
    return {
        play: async function (o) {
            stop();
            p = { players: [] };
            var agent = o.lines.filter(function (l) { return l.track === 'agent'; });
            var customer = o.lines.filter(function (l) { return l.track === 'customer'; });
            p.players = await Promise.all([playTrack(o, agent, o.startMs), playTrack(o, customer, o.startMs)]);
        },
        stop: stop
    };
})();
