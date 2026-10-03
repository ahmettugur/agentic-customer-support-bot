using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Eskalasyon kayıtları için secondary port.
///</summary>
public interface IEscalationSink
{
    /// <summary>
    /// Eskalasyonu kaydeder. Aynı session + ajan için zaten açık (Open/Acknowledged) bir kayıt
    /// varsa YENİ kayıt oluşturulmaz, mevcut kayıt döner — çağıran dönen <c>Id</c>'yi kendi
    /// isteğinin <c>Id</c>'siyle karşılaştırarak kaydın yeni oluşup oluşmadığını anlar.
    /// Yazma bilinçli olarak CancellationToken almaz: istemci bağlantıyı kesse bile cache ile DB tutarlı kalmalı.
    /// </summary>
    Task<EscalationRequest> CreateAsync(EscalationRequest request);
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

    /// <summary>
    /// Karar (acknowledge/resolve/dismiss) uygular. Geçiş geçersizse ya da kayıt bu arada başka
    /// bir çağrı tarafından değiştirildiyse <c>false</c> döner — aynı kaydı yarışan iki karardan
    /// yalnızca biri uygulanır.
    /// </summary>
    Task<bool> DecideAsync(string id, string action, string? assignedTo = null, string? resolution = null);

    event EventHandler<EscalationRequest>? RequestCreated;
    event EventHandler<EscalationRequest>? RequestDecided;
}
