namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class ComplaintEntity
{
    public long Code { get; set; }
    public long OrderId { get; set; }
    public long CustomerId { get; set; }
    public string Complaint { get; set; } = "";
    public string Status { get; set; } = "";
}
