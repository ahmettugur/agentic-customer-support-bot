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
    ///
    /// <para>
    /// <b>Dönüş değeri</b>: ipucu kuyruğa girdiyse <c>true</c>, ambient bağlamda session
    /// olmadığı için düştüyse <c>false</c>. Bu ayrım kozmetik değil — ipucu düştüğünde
    /// ekranda hiçbir şey belirmez, dolayısıyla çağıran tool LLM'e "kullanıcıya gösterildi"
    /// diyemez. Sesli (native realtime) kanalda ambient bağlam hiç kurulmadığı için bu yol
    /// gerçekten yürünüyor; bkz. <c>ProductToolsService.ProductListTool</c>.
    /// </para>
    /// </summary>
    bool Emit(StreamEvent evt);

    /// <summary>
    /// Verilen session'a ait bekleyen tüm ipuçlarını okuyup kuyruğu temizler.
    /// </summary>
    IReadOnlyList<StreamEvent> DrainPending(string sessionId);
}
