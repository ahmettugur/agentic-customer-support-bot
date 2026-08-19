using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Eskalasyon kayıtları için secondary port.
///</summary>
public interface IEscalationSink
{
    EscalationRequest Create(EscalationRequest request);
    IReadOnlyList<EscalationRequest> GetOpen();
    IReadOnlyList<EscalationRequest> GetRecent(int count = 50);
    EscalationRequest? Get(string id);

    /// <summary>
    /// Bir agent'ın görebileceği son <paramref name="count"/> eskalasyon: atanmamış VEYA ona
    /// atanmış olanlar, en yeniden eskiye.
    ///
    /// <para>
    /// Diğer okumaların aksine bu metot <b>async</b>'tir çünkü cache üzerinden cevaplanamaz.
    /// <see cref="GetRecent"/> yalnızca hydrate edilmiş kayıtları görür (Postgres adaptöründe:
    /// açık olanlar + son N kapalı); bir agent'ın kendi kapalı kaydı o pencerenin gerisinde
    /// kalabilir. Filtreyi cache üzerinde uygulamak sınırı ötelemekten ibarettir — hem daraltma
    /// hem limit veri kaynağında yapılmalıdır.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default);
    bool Decide(string id, string action, string? assignedTo = null, string? resolution = null);

    event EventHandler<EscalationRequest>? RequestCreated;
    event EventHandler<EscalationRequest>? RequestDecided;
}
