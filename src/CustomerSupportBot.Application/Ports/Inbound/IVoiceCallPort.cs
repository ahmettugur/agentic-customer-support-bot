// Ports/Inbound/IVoiceCallPort.cs
// Temsilci–müşteri sesli görüşmesi: başlatma, rıza/kabul, sinyal aktarımı, kayıt parçaları, dinleme.

using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>Görüşmeyi yürüten personel: Agent rolünde bağlı temsilci kaydı, yönetici için kullanıcı kimliği.</summary>
public sealed record StaffCaller(string AgentId, string DisplayName, bool IsAdmin);

public enum VoiceCallError { NotFound, Forbidden, Busy, NotInHumanMode, InvalidState, Invalid, TooLarge, Disabled }

public sealed record VoiceCallResult(VoiceCall? Call, VoiceCallError? Error = null)
{
    public bool Ok => Error is null;
}

public sealed record IceServerConfig(IReadOnlyList<IceServer> IceServers);
public sealed record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);
public sealed record VoiceTranscriptLine(string ChunkId, string Track, int OffsetMs, string? Text, string Status);
public sealed record VoiceCallView(VoiceCall Call, IReadOnlyList<VoiceTranscriptLine> Lines);

public interface IVoiceCallPort
{
    Task<VoiceCallResult> StartAsync(string sessionId, StaffCaller staff, CancellationToken ct = default);
    Task<VoiceCallResult> AcceptAsync(string callId, string? customerId, CancellationToken ct = default);
    Task<VoiceCallResult> DeclineAsync(string callId, string? customerId, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> HangupByStaffAsync(string callId, StaffCaller staff, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> HangupByCustomerAsync(string callId, string? customerId, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> SignalFromStaffAsync(string callId, StaffCaller staff, string payloadJson, CancellationToken ct = default);
    Task<VoiceCallResult> SignalFromCustomerAsync(string callId, string? customerId, string payloadJson, CancellationToken ct = default);
    Task<VoiceCallResult> UploadChunkAsync(string callId, StaffCaller staff, VoiceTrack track, int sequence, int offsetMs,
        int durationMs, string contentType, byte[] data, CancellationToken ct = default);
    Task<VoiceCall?> GetOpenForStaffAsync(StaffCaller staff, CancellationToken ct = default);
    Task<(VoiceCallView? View, VoiceCallError? Error)> GetViewAsync(string callId, StaffCaller staff, CancellationToken ct = default);
    Task<(VoiceRecordingChunk? Chunk, VoiceCallError? Error)> GetChunkAudioAsync(string callId, string chunkId, StaffCaller staff, CancellationToken ct = default);
    Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForStaffAsync(string callId, StaffCaller staff, CancellationToken ct = default);
    Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForCustomerAsync(string callId, string? customerId, CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Zaman aşımı süpürmesi: çalan → Missed (45 sn), parçası kesilen aktif → Failed (60 sn). Kapatılan sayı.</summary>
    Task<int> SweepAsync(CancellationToken ct = default);
}
