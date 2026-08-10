using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Sipariş yönetimi araçları için secondary port.
/// </summary>
public interface IOrderToolsService
{
    ToolResult OrderPlacementTool(string productName, int? quantity, string customerId);

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
