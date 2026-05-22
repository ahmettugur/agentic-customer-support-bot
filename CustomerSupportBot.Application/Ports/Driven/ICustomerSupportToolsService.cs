using System.ComponentModel;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Müşteri destek AI araçları port'u.
/// Adapter'lar bu arayüz üzerinden tool fonksiyonlarına erişir.
/// </summary>
public interface ICustomerSupportToolsService
{
    ToolResult ProductInquiryTool(string productName);
    ToolResult OrderPlacementTool(string productName, int? quantity, string customerId);
    ToolResult OrderStatusTool(string orderId);
    ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId);
    ToolResult GetLastOrderTool(string customerId);
    ToolResult GetAllOrdersTool(string customerId);
}
