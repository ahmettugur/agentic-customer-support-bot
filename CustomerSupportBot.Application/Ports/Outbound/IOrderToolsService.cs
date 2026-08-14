using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Sipariş yönetimi araçları için secondary port.
/// </summary>
public interface IOrderToolsService
{
    /// <summary>
    /// Tek bir siparişte <b>bir veya daha fazla</b> ürün satırı oluşturur.
    ///
    /// <para>
    /// <paramref name="lines"/> LLM'in ürettiği ham taleptir: ürün adları doğrulanmamıştır,
    /// aynı ürün birden fazla kez geçebilir, adetler geçersiz olabilir. Doğrulama, katalog
    /// çözümlemesi ve tekilleştirme bu metodun içinde yapılır.
    /// </para>
    /// </summary>
    ToolResult OrderPlacementTool(IReadOnlyList<OrderLineRequest> lines, string customerId);

    /// <summary>
    /// Salt-okunur ön kontrol: sipariş var mı ve login'li müşteriye ait mi? Engel varsa
    /// kullanıcıya dönecek <see cref="ToolResult"/>, yoksa <c>null</c>.
    ///
    /// <para>
    /// HITL onaylı tool'larda (iptal/iade/şikayet) gerçek iş admin kararından SONRA çalışır;
    /// sahiplik ihlali orada yakalanırsa talep önce admin kuyruğuna düşer, admin onaylar ve
    /// işlem sessizce başarısız olur. Bu metot aynı kontrolü onay kaydı OLUŞTURULMADAN önce
    /// yapıp kullanıcıya anında geri bildirim verir ve kuyruğu kirletmez. Yürütme anındaki
    /// kontrolün YERİNE geçmez — durum iki an arasında değişebilir, ikisi birlikte çalışır.
    /// </para>
    /// </summary>
    ToolResult? ValidateOrderActionable(string orderId, string customerId);

    /// <summary>
    /// <paramref name="customerId"/> LLM parametresi DEĞİL — çağıran taraf (ApprovalGateService)
    /// bunu her zaman login'li kullanıcının doğrulanmış kimliğinden geçirir. Sipariş başka bir
    /// müşteriye aitse <see cref="WellKnown.ToolErrorCodes.CustomerIdMismatch"/> ile reddedilir.
    /// </summary>
    ToolResult OrderStatusTool(string orderId, string customerId);
    ToolResult GetLastOrderTool(string customerId);
    ToolResult GetAllOrdersTool(string customerId);

    /// <summary>
    /// <paramref name="customerId"/> LLM parametresi DEĞİL — bkz. <see cref="OrderStatusTool"/>.
    /// </summary>
    ToolResult OrderCancelTool(string orderId, string reason, string customerId);

    /// <summary>
    /// <paramref name="customerId"/> LLM parametresi DEĞİL — bkz. <see cref="OrderStatusTool"/>.
    /// </summary>
    ToolResult ReturnRequestTool(string orderId, string reason, string customerId);
}
