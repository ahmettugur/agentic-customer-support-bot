using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Sesli görüşme kayıtları. Temsilci ve oturum başına tek açık görüşme kuralını depo garanti eder.</summary>
public interface IVoiceCallStore
{
    /// <summary>Ekler; temsilcinin ya da oturumun açık (Ringing/Active) görüşmesi varsa eklemez, <c>false</c> döner.</summary>
    Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default);
    Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default);
    Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Kalıcı durum <paramref name="expectedStatus"/> ise yazar (koşullu güncelleme); değilse <c>false</c>.</summary>
    Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default);

    Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default);
}
