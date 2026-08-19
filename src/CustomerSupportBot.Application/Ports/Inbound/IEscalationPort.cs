using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Eskalasyon yönetimi için primary port.
/// </summary>
public interface IEscalationPort
{
    /// <summary>Yeni eskalasyon kaydı oluşturur.</summary>
    EscalationRequest Create(EscalationRequest request);

    /// <summary>Açık eskalasyonlar.</summary>
    IReadOnlyList<EscalationRequest> GetOpen();

    /// <summary>Son N eskalasyon.</summary>
    IReadOnlyList<EscalationRequest> GetRecent(int count = 50);

    /// <summary>
    /// Bir agent'ın görebileceği son <paramref name="count"/> eskalasyon: atanmamış VEYA ona
    /// atanmış olanlar.
    ///
    /// <para>
    /// Daraltma ve limit birlikte, <b>veri kaynağında</b> uygulanır. Önce son N kaydı alıp
    /// sonra elemek yanlış sonuç verir: o N kaydın tamamı başka agent'lara aitse liste boş
    /// döner, oysa daha gerisinde çağıranın kendi kaydı vardır. Aynı sebeple cache üzerinden
    /// de cevaplanamaz — cache'in kendisi bir "son N" penceresidir.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default);

    /// <summary>Tek eskalasyon kaydı.</summary>
    EscalationRequest? Get(string id);

    /// <summary>Admin kararı: "acknowledge", "resolve", "dismiss".</summary>
    bool Decide(string id, string action, string? assignedTo = null, string? resolution = null);

    event EventHandler<EscalationRequest>? RequestCreated;
    event EventHandler<EscalationRequest>? RequestDecided;
}
