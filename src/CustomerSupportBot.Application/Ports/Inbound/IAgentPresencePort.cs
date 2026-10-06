using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Temsilcinin anlık durumu (<see cref="Presence"/> = geçerli durum, zaman aşımı uygulanmış;
/// <see cref="ChosenPresence"/> = temsilcinin seçtiği).
/// </summary>
public sealed record AgentPresenceInfo(
    string AgentId,
    string DisplayName,
    AgentPresence Presence,
    AgentPresence ChosenPresence,
    DateTime? Since,
    DateTime? LastSeenAt,
    int CurrentLoad,
    int MaxConcurrentLoad,
    // Sesli görüşmede mi — varlık uç noktası görüşme servisinden doldurur.
    bool InVoiceCall = false);

/// <summary>Temsilci çevrimiçi/uzakta durumu. Bilinmeyen temsilci için tüm işlemler <c>null</c> döner.</summary>
public interface IAgentPresencePort
{
    AgentPresenceInfo? Get(string agentId);

    /// <summary>Temsilcinin seçimi.</summary>
    AgentPresenceInfo? Set(string agentId, AgentPresence presence);

    /// <summary>
    /// Panel açılışı: seçilen durum çevrimdışıysa çevrimiçi yapılır; çevrimiçi/uzakta ise korunur
    /// (molaya "uzakta" çıkan temsilci sayfayı yenileyince çevrimiçine dönmez).
    /// </summary>
    AgentPresenceInfo? Connect(string agentId);

    /// <summary>Panel açıkken periyodik kalp atışı; seçimi değiştirmez.</summary>
    AgentPresenceInfo? Heartbeat(string agentId);

    /// <summary>Aktif temsilciler: önce çevrimiçi, sonra uzakta, sonra çevrimdışı; ada göre.</summary>
    IReadOnlyList<AgentPresenceInfo> GetAll();
}
