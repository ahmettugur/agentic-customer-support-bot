using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Tool fonksiyonlarından streaming pipeline'a UI ipuçları gönderir.
/// Only an active streaming turn can accept hints; customer identity comes from IApprovalContextAccessor.
/// </summary>
public interface IUiHintEmitter
{
    /// <summary>Opens a streaming turn. Disposal discards any undelivered hints.</summary>
    IUiHintTurn BeginTurn(string? sessionId);

    /// <summary>
    /// Bir UI ipucunu mevcut streaming turun kuyruğuna ekler.
    /// Session ID'yi IApprovalContextAccessor'dan otomatik alır.
    ///
    /// <para>
    /// <b>Dönüş değeri</b>: ipucu kuyruğa girdiyse <c>true</c>, ambient bağlamda session
    /// veya aktif streaming kapsamı olmadığı için düştüyse <c>false</c>. Bu ayrım kozmetik değil — ipucu düştüğünde
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

/// <summary>A turn owns its buffer; each async iterator step activates that same buffer.</summary>
public interface IUiHintTurn : IDisposable
{
    IDisposable Activate();
}
