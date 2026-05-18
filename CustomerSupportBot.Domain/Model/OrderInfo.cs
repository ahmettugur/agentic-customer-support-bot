// Core/Model/OrderInfo.cs
// Domain modeli — sipariş bilgisi.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Sipariş domain modeli.
/// </summary>
public class OrderInfo
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public string CustomerId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; } = DateTime.Now;
}
