// Application/Services/Privacy/EpisodicMemoryEraser.cs
// Kişisel veri silme — episodik bellekteki (vektör deposu) oturum ve müşteri kayıtları.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Memory;

namespace CustomerSupportBot.Application.Services.Privacy;

/// <summary>
/// Episodik bellek her tamamlanan turun soru/yanıt özetini oturum kimliği ve (girişli müşteride)
/// müşteri etiketiyle saklar. Oturum silinince o oturumun kayıtları, müşteri silme talebinde ayrıca
/// müşteri etiketli tüm kayıtlar silinir. Vektör deposu silinen sayıyı döndürmez; dönen sayı işlenen
/// oturum sayısıdır. Bellek kapalıyken işlem yapılmaz.
/// </summary>
public sealed class EpisodicMemoryEraser(SemanticMemoryService memory) : ISessionDataEraser, ICustomerDataEraser
{
    public string Name => "episodic-memory";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        await memory.DeleteEpisodesForSessionsAsync(sessionIds, ct);
        return memory.Enabled ? sessionIds.Count : 0;
    }

    public async Task<int> EraseCustomerAsync(string customerId, CancellationToken ct = default)
    {
        await memory.DeleteEpisodesForCustomerAsync(customerId, ct);
        return 0;
    }
}
