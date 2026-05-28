// Application/Services/Tools/CustomerSupportToolsService.cs
// ICustomerSupportToolsService facade'ı — sub-service'lere delegate eder.

using System.ComponentModel;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

/// <summary>
/// ICustomerSupportToolsService facade implementasyonu.
/// Tüm çağrıları ProductToolsService, OrderToolsService ve ComplaintToolsService'e iletir.
/// </summary>
public sealed class CustomerSupportToolsService : ICustomerSupportToolsService
{
    private readonly IProductToolsService _product;
    private readonly IOrderToolsService _order;
    private readonly IComplaintToolsService _complaint;

    public CustomerSupportToolsService(
        IProductToolsService product,
        IOrderToolsService order,
        IComplaintToolsService complaint)
    {
        _product   = product;
        _order     = order;
        _complaint = complaint;
    }

    // ─── IProductToolsService ───
    public ToolResult ProductInquiryTool(string productName)
        => _product.ProductInquiryTool(productName);

    public ToolResult ProductListTool(string? category = null)
        => _product.ProductListTool(category);

    // ─── IOrderToolsService ───
    public ToolResult OrderPlacementTool(string productName, int? quantity, string customerId)
        => _order.OrderPlacementTool(productName, quantity, customerId);

    public ToolResult OrderStatusTool(string orderId)
        => _order.OrderStatusTool(orderId);

    public ToolResult GetLastOrderTool(string customerId)
        => _order.GetLastOrderTool(customerId);

    public ToolResult GetAllOrdersTool(string customerId)
        => _order.GetAllOrdersTool(customerId);

    public ToolResult OrderCancelTool(string orderId, string reason)
        => _order.OrderCancelTool(orderId, reason);

    public ToolResult ReturnRequestTool(string orderId, string reason)
        => _order.ReturnRequestTool(orderId, reason);

    // ─── IComplaintToolsService ───
    public ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId = null)
        => _complaint.ComplaintRegistrationTool(orderId, complaintText, customerId);

    // ─── HumanHandoff (yan etkisiz, bağımlılıksız) ───
    [Description("Kullanıcıyı bir müşteri temsilcisine yönlendirme talebini kaydeder. " +
                 "Tool yan etkisi yoktur — yalnızca niyeti formalize eder.")]
    public static ToolResult HumanHandoffTool(
        [Description("Kullanıcının temsilciyle görüşme isteme sebebi (1-2 cümle).")] string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ToolResult.ValidationError("Handoff talebi için sebep alanı doldurulmalı.", WellKnown.ToolParameterNames.Reason);

        return ToolResult.Ok(
            message: "Temsilci yönlendirme talebiniz alındı. Kısa süre içinde bir temsilci sizinle iletişime geçecek.",
            data: new { handoffRequested = true, reason = reason.Trim() },
            confidence: 1.0);
    }
}
