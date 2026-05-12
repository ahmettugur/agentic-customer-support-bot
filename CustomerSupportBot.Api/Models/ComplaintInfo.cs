namespace CustomerSupportBot.Models;

/// <summary>
/// Şikayet bilgisini temsil eder.
/// </summary>
public class ComplaintInfo
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Complaint { get; set; } = "";
    public string Status { get; set; } = "";
}