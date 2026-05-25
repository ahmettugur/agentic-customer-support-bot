using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Sipariş yönetimi araçları için secondary port.
/// </summary>
public interface IOrderToolsService
{
    ToolResult OrderPlacementTool(string productName, int? quantity, string customerId);
    ToolResult OrderStatusTool(string orderId);
    ToolResult GetLastOrderTool(string customerId);
    ToolResult GetAllOrdersTool(string customerId);
    ToolResult OrderCancelTool(string orderId, string reason);
    ToolResult ReturnRequestTool(string orderId, string reason);
}
