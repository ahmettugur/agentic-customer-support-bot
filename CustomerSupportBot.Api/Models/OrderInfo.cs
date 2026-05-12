namespace CustomerSupportBot.Models;

/// <summary>
/// Sipariş bilgisini temsil eder.
/// </summary>
public class OrderInfo
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public string CustomerId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; } = DateTime.Now;
}