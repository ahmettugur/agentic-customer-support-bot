using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Şikayet yönetimi araçları için secondary port.
/// </summary>
public interface IComplaintToolsService
{
    ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId);
}
