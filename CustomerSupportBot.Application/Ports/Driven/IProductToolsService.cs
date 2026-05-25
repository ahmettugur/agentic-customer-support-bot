using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Ürün sorgulama araçları için secondary port.
/// </summary>
public interface IProductToolsService
{
    ToolResult ProductInquiryTool(string productName);
    ToolResult ProductListTool(string? category = null);
}
