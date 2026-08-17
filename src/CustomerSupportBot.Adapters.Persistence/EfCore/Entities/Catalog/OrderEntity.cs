namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class OrderEntity
{
    public long Code { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    public DateTime? ReturnRequestedAt { get; set; }
    public string? ReturnReason { get; set; }

    public ICollection<OrderDetailEntity> Details { get; set; } = [];
}
