using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Tool fonksiyonlarından streaming pipeline'a UI ipuçları gönderir.
/// Session ID tabanlı ConcurrentDictionary kullanır; AsyncLocal yerine
/// IApprovalContextAccessor üzerinden session ID okur — SDK uyumlu, güvenilir.
/// </summary>
public interface IUiHintEmitter
{
    /// <summary>
    /// Bir UI ipucunu mevcut session'ın kuyruğuna ekler.
    /// Session ID'yi IApprovalContextAccessor'dan otomatik alır.
    /// </summary>
    void Emit(StreamEvent evt);

    /// <summary>
    /// Verilen session'a ait bekleyen tüm ipuçlarını okuyup kuyruğu temizler.
    /// </summary>
    IReadOnlyList<StreamEvent> DrainPending(string sessionId);
}
