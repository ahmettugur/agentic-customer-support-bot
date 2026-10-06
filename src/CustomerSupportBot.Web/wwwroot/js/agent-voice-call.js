// agent-voice-call.js — temsilci ↔ müşteri sesli görüşmesi (WebRTC). Sinyal: POST …/signal, gelen sinyal SSE
// `voice_signal` olayıyla bu modüle verilir. Kayıt YALNIZCA temsilci tarafında: yerel mikrofon ve uzak müşteri
// sesi ayrı ayrı, 10 sn'lik tam webm parçaları (kaydedici her parçada yeniden başlar) halinde yüklenir.
(function () {
    'use strict';
    var SEGMENT_MS = 10000;
    var CONNECT_TIMEOUT_MS = 20000;
    var DISCONNECT_GRACE_MS = 30000;

    var s = null; // aktif görüşme durumu

    function reset() {
        if (!s) return;
        try { (s.recorders || []).forEach(function (r) { r.stopped = true; if (r.rec && r.rec.state !== 'inactive') r.rec.stop(); }); } catch (e) { }
        try { if (s.pc) s.pc.close(); } catch (e) { }
        try { if (s.local) s.local.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { }
        if (s.audio) { s.audio.srcObject = null; s.audio.remove(); }
        clearTimeout(s.connectTimer); clearTimeout(s.disconnectTimer);
        s = null;
    }

    function notify(method, arg) { if (s && s.ref) s.ref.invokeMethodAsync(method, arg).catch(function () { }); }

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
                notify('OnVoiceState', 'connected');
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
        s.callStart = performance.now();
        s.recorders = [segmentRecorder(s.local, 'agent'), segmentRecorder(s.remote, 'customer')];
        notify('OnVoiceRecording', true);
    }

    function segmentRecorder(stream, track) {
        var mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus' : 'audio/ogg;codecs=opus';
        var state = { seq: 0, stopped: false, rec: null };
        function next() {
            if (state.stopped || !s) return;
            var startedAt = performance.now();
            var rec = new MediaRecorder(stream, { mimeType: mime, audioBitsPerSecond: 32000 });
            state.rec = rec;
            var parts = [];
            rec.ondataavailable = function (e) { if (e.data && e.data.size) parts.push(e.data); };
            rec.onstop = function () {
                var blob = new Blob(parts, { type: mime.split(';')[0] });
                var offset = Math.max(0, Math.round(startedAt - s.callStart));
                var duration = Math.round(performance.now() - startedAt);
                upload(track, state.seq++, offset, duration, blob);
                next();
            };
            rec.start();
            setTimeout(function () { if (rec.state !== 'inactive') rec.stop(); }, SEGMENT_MS);
        }
        next();
        return state;
    }

    async function upload(track, seq, offsetMs, durationMs, blob, attempt) {
        if (!blob.size || !s) return;
        attempt = attempt || 0;
        var path = '/voice-calls/' + encodeURIComponent(s.callId) + '/chunks?track=' + track + '&seq=' + seq
            + '&offsetMs=' + offsetMs + '&durationMs=' + durationMs;
        try {
            var r = await post(path, blob, blob.type);
            if (r.ok || r.status === 409 || r.status === 403) return;
            throw new Error('HTTP ' + r.status);
        } catch (e) {
            if (attempt < 3) setTimeout(function () { upload(track, seq, offsetMs, durationMs, blob, attempt + 1); }, 1000 * (attempt + 1));
            else notify('OnVoiceError', 'Kayıt kesintiye uğradı.');
        }
    }

    window.csbVoice = {
        isActive: function () { return !!s; },

        staffStart: function (o) {
            reset();
            s = { role: 'staff', apiBase: o.apiBase, token: o.token, callId: o.callId, ref: o.dotnetRef,
                  signalPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/signal',
                  hangupPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/hangup',
                  icePath: '/voice-calls/' + encodeURIComponent(o.callId) + '/ice-config' };
            notify('OnVoiceState', 'ringing');
        },

        staffOnSignal: async function (json) {
            if (!s || s.role !== 'staff') return;
            var m = typeof json === 'string' ? JSON.parse(json) : json;
            if (m.callId !== s.callId) return;
            if (m.type === 'accepted') {
                try { s.local = await getMic(); }
                catch (e) { notify('OnVoiceError', 'Mikrofon izni verilmedi.'); window.csbVoice.hangup('agent_hangup'); return; }
                notify('OnVoiceState', 'connecting');
                var pc = await createPeer();
                var offer = await pc.createOffer();
                await pc.setLocalDescription(offer);
                sendSignal('offer', pc.localDescription.toJSON());
            } else if (m.type === 'answer') {
                await s.pc.setRemoteDescription(m.data); await flushIce();
            } else if (m.type === 'ice') {
                await addIce(m.data);
            } else if (m.type === 'declined' || m.type === 'ended') {
                notify('OnVoiceState', 'ended'); reset();
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
            notify('OnVoiceState', 'connecting');
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
                notify('OnVoiceState', 'ended'); reset();
            }
        },

        hangup: async function (reason) {
            if (!s) return;
            var path = s.hangupPath + '?reason=' + encodeURIComponent(reason || '');
            var ref = s.ref;
            // Son kayıt parçaları kapanmadan yüklensin diye kaydediciler önce durdurulur (onstop yükler).
            (s.recorders || []).forEach(function (r) { r.stopped = true; if (r.rec && r.rec.state !== 'inactive') r.rec.stop(); });
            try { await post(path); } catch (e) { }
            if (ref) ref.invokeMethodAsync('OnVoiceState', 'ended').catch(function () { });
            setTimeout(reset, 1500);
        },

        setMuted: function (muted) {
            if (s && s.local) s.local.getAudioTracks().forEach(function (t) { t.enabled = !muted; });
        }
    };
})();
