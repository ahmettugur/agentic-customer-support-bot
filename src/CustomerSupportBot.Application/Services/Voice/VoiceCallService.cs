// Temsilci–müşteri sesli görüşmesi: durum, yetki, sinyal aktarımı, kayıt parçaları, zaman aşımları.
// Ses buradan geçmez (WebRTC P2P); sinyaller köprüden yönlü ve kalıcılaştırılmadan aktarılır.

using System.Globalization;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Voice;

public sealed class VoiceCallService(
    IVoiceCallStore calls,
    IVoiceRecordingStore recordings,
    IChatBridge bridge,
    IChatModeRegistry modes,
    ISessionManager sessions,
    IOptionsMonitor<VoiceCallOptions> options,
    TimeProvider time,
    ILogger<VoiceCallService> logger) : IVoiceCallPort
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<VoiceCallResult> StartAsync(string sessionId, StaffCaller staff, CancellationToken ct = default)
    {
        if (!options.CurrentValue.Enabled) return new(null, VoiceCallError.Disabled);
        if (modes.GetMode(sessionId) != ChatMode.Human) return new(null, VoiceCallError.NotInHumanMode);

        var call = VoiceCall.Start(sessionId, staff.AgentId, staff.DisplayName, Now);
        if (!await calls.TryCreateAsync(call, ct)) return new(null, VoiceCallError.Busy);

        bridge.PublishVoiceSignal(sessionId, toCustomer: true,
            Json(new { callId = call.Id, type = "ring", agentName = staff.DisplayName }));
        logger.LogInformation("[VoiceCall] Çalıyor call={Call} session={Sid} agent={Agent}", call.Id, sessionId, staff.AgentId);
        return new(call);
    }

    public async Task<VoiceCallResult> AcceptAsync(string callId, string? customerId, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return await TransitionAsync(call!, c => c.Accept(Now), ct, onSuccess: c =>
            bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, Json(new { callId = c.Id, type = "accepted" })));
    }

    public async Task<VoiceCallResult> DeclineAsync(string callId, string? customerId, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        var why = reason == VoiceCallEndReasons.NoMicrophone ? VoiceCallEndReasons.NoMicrophone : VoiceCallEndReasons.Declined;
        return await TransitionAsync(call!, c => c.Decline(Now, why), ct, onSuccess: c =>
            bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, Json(new { callId = c.Id, type = "declined", reason = why })));
    }

    public async Task<VoiceCallResult> HangupByStaffAsync(string callId, StaffCaller staff, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        if (error is not null) return new(null, error);
        return await HangupAsync(call!, NormalizeReason(reason, VoiceCallEndReasons.AgentHangup), by: "agent", ct);
    }

    public async Task<VoiceCallResult> HangupByCustomerAsync(string callId, string? customerId, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return await HangupAsync(call!, NormalizeReason(reason, VoiceCallEndReasons.CustomerHangup), by: "customer", ct);
    }

    public async Task<VoiceCallResult> SignalFromStaffAsync(string callId, StaffCaller staff, string payloadJson, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        if (error is not null) return new(null, error);
        return Relay(call!, payloadJson, toCustomer: true);
    }

    public async Task<VoiceCallResult> SignalFromCustomerAsync(string callId, string? customerId, string payloadJson, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return Relay(call!, payloadJson, toCustomer: false);
    }

    public async Task<VoiceCallResult> UploadChunkAsync(string callId, StaffCaller staff, VoiceTrack track, int sequence,
        int offsetMs, int durationMs, string contentType, byte[] data, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (data.Length == 0 || sequence < 0 || offsetMs < 0 || durationMs <= 0) return new(null, VoiceCallError.Invalid);
        if (data.Length > o.MaxChunkBytes) return new(null, VoiceCallError.TooLarge);

        var call = await calls.GetAsync(callId, ct);
        if (call is null) return new(null, VoiceCallError.NotFound);
        if (call.AgentId != staff.AgentId) return new(null, VoiceCallError.Forbidden);
        var lateOk = call.EndedAt is { } ended && Now - ended <= TimeSpan.FromSeconds(o.LateChunkGraceSeconds) && call.ConsentAt is not null;
        if (call.Status != VoiceCallStatus.Active && !lateOk) return new(null, VoiceCallError.InvalidState);

        await recordings.TryAddAsync(new VoiceRecordingChunk
        {
            CallId = call.Id, SessionId = call.SessionId, Track = track, Sequence = sequence,
            OffsetMs = offsetMs, DurationMs = durationMs, ContentType = contentType, Data = data,
            CreatedAt = Now, NextAttemptAt = Now
        }, ct);
        await calls.TouchChunkAsync(call.Id, Now, ct);
        return new(call);
    }

    public Task<VoiceCall?> GetOpenForStaffAsync(StaffCaller staff, CancellationToken ct = default) =>
        calls.GetOpenForAgentAsync(staff.AgentId, ct);

    public async Task<(VoiceCallView? View, VoiceCallError? Error)> GetViewAsync(string callId, StaffCaller staff, CancellationToken ct = default)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        if (!staff.IsAdmin && call.AgentId != staff.AgentId) return (null, VoiceCallError.Forbidden);
        var lines = (await recordings.ListMetaAsync(callId, ct))
            .Select(c => new VoiceTranscriptLine(c.Id, TrackName(c.Track), c.OffsetMs, c.TranscriptText, c.TranscriptStatus.ToString()))
            .ToList();
        return (new VoiceCallView(call, lines), null);
    }

    public async Task<(VoiceRecordingChunk? Chunk, VoiceCallError? Error)> GetChunkAudioAsync(string callId, string chunkId, StaffCaller staff, CancellationToken ct = default)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        if (!staff.IsAdmin && call.AgentId != staff.AgentId) return (null, VoiceCallError.Forbidden);
        var chunk = await recordings.GetAsync(chunkId, ct);
        if (chunk is null || chunk.CallId != callId || chunk.Data.Length == 0) return (null, VoiceCallError.NotFound);
        return (chunk, null);
    }

    public async Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForStaffAsync(string callId, StaffCaller staff, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        return error is not null ? (null, error) : (BuildIceConfig(call!.Id), null);
    }

    public async Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForCustomerAsync(string callId, string? customerId, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        return error is not null ? (null, error) : (BuildIceConfig(call!.Id), null);
    }

    public Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default) =>
        calls.ListForSessionAsync(sessionId, ct);

    public async Task<IReadOnlySet<string>> GetAgentsInCallAsync(CancellationToken ct = default) =>
        (await calls.ListOpenAsync(ct)).Select(c => c.AgentId).ToHashSet(StringComparer.Ordinal);

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var closed = 0;
        foreach (var call in await calls.ListOpenAsync(ct))
        {
            if (call.Status == VoiceCallStatus.Ringing && Now - call.CreatedAt >= TimeSpan.FromSeconds(o.RingTimeoutSeconds))
            {
                var r = await TransitionAsync(call, c => c.Miss(Now), ct, onSuccess: c => NotifyBoth(c, "missed"));
                if (r.Ok) closed++;
            }
            else if (call.Status == VoiceCallStatus.Active
                     && Now - (call.LastChunkAt ?? call.AnsweredAt ?? call.CreatedAt) >= TimeSpan.FromSeconds(o.ChunkStaleSeconds))
            {
                var r = await HangupAsync(call, VoiceCallEndReasons.ConnectionLost, by: "system", ct);
                if (r.Ok) closed++;
            }
        }
        return closed;
    }

    // ── yardımcılar ───────────────────────────────────────────────────────────

    private async Task<VoiceCallResult> HangupAsync(VoiceCall call, string reason, string by, CancellationToken ct)
    {
        if (!call.IsOpen) return new(call);   // iki taraf aynı anda kapattı — sessizce kabul
        var wasActive = call.Status == VoiceCallStatus.Active;
        var result = await TransitionAsync(call, c => c.Hangup(Now, reason), ct, onSuccess: c =>
        {
            var payload = Json(new { callId = c.Id, type = "ended", reason, by });
            if (by != "customer") bridge.PublishVoiceSignal(c.SessionId, toCustomer: true, payload);
            if (by != "agent") bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, payload);
        });
        if (result.Ok && wasActive && result.Call!.Duration is { } d)
            await WriteDurationNoteAsync(result.Call, d);
        return result;
    }

    // Not yazılamazsa görüşme yine biter (durum zaten kalıcı); hata yalnızca loglanır.
    private async Task WriteDurationNoteAsync(VoiceCall call, TimeSpan duration)
    {
        try { await bridge.PublishSystemMessageAsync(call.SessionId, $"Sesli görüşme · {FormatDuration(duration)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "[VoiceCall] Süre notu yazılamadı call={Call} session={Sid}", call.Id, call.SessionId); }
    }

    private async Task<VoiceCallResult> TransitionAsync(VoiceCall call, Action<VoiceCall> change, CancellationToken ct, Action<VoiceCall> onSuccess)
    {
        var expected = call.Status;
        try { change(call); }
        catch (VoiceCallStateException) { return new(null, VoiceCallError.InvalidState); }

        if (!await calls.TryUpdateAsync(call, expected, ct)) return new(null, VoiceCallError.InvalidState);
        onSuccess(call);
        return new(call);
    }

    private VoiceCallResult Relay(VoiceCall call, string payloadJson, bool toCustomer)
    {
        if (!call.IsOpen) return new(null, VoiceCallError.InvalidState);
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("callId", out var id) || id.GetString() != call.Id
                || !doc.RootElement.TryGetProperty("type", out var type) || type.GetString() is not ("offer" or "answer" or "ice"))
                return new(null, VoiceCallError.Invalid);
        }
        catch (JsonException) { return new(null, VoiceCallError.Invalid); }

        bridge.PublishVoiceSignal(call.SessionId, toCustomer, payloadJson);
        return new(call);
    }

    private void NotifyBoth(VoiceCall call, string type)
    {
        var payload = Json(new { callId = call.Id, type = "ended", reason = type, by = "system" });
        bridge.PublishVoiceSignal(call.SessionId, toCustomer: true, payload);
        bridge.PublishVoiceSignal(call.SessionId, toCustomer: false, payload);
    }

    private async Task<(VoiceCall? Call, VoiceCallError? Error)> LoadForCustomerAsync(string callId, string? customerId, CancellationToken ct)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        var session = await sessions.GetAsync(call.SessionId, ct);
        if (string.IsNullOrEmpty(customerId) || session is null
            || !string.Equals(session.State.AuthenticatedCustomerId, customerId, StringComparison.Ordinal))
            return (null, VoiceCallError.Forbidden);
        return (call, null);
    }

    private async Task<(VoiceCall? Call, VoiceCallError? Error)> LoadForStaffActionAsync(string callId, StaffCaller staff, CancellationToken ct)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        // Görüşmeyi yalnızca başlatan temsilci yürütür; yönetici dinleyebilir (GetView) ama yürütemez.
        return call.AgentId == staff.AgentId ? (call, null) : (null, VoiceCallError.Forbidden);
    }

    private IceServerConfig BuildIceConfig(string callId)
    {
        var o = options.CurrentValue;
        var servers = new List<IceServer>();
        if (o.StunUrls.Count > 0) servers.Add(new IceServer(o.StunUrls));
        if (o.Turn.Urls.Count > 0 && !string.IsNullOrWhiteSpace(o.Turn.SharedSecret))
        {
            var (user, cred) = TurnCredentialFactory.Create(o.Turn.SharedSecret, callId, time.GetUtcNow(),
                TimeSpan.FromMinutes(o.Turn.CredentialTtlMinutes));
            servers.Add(new IceServer(o.Turn.Urls, user, cred));
        }
        return new IceServerConfig(servers);
    }

    private static string NormalizeReason(string? reason, string fallback) => reason switch
    {
        VoiceCallEndReasons.ConnectionLost or VoiceCallEndReasons.ConnectFailed => reason,
        _ => fallback
    };

    public static string TrackName(VoiceTrack track) => track == VoiceTrack.Agent ? "agent" : "customer";

    internal static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalMinutes} dk {d.Seconds} sn")
            : string.Create(CultureInfo.InvariantCulture, $"{d.Seconds} sn");

    private static string Json(object o) => JsonSerializer.Serialize(o);
}
