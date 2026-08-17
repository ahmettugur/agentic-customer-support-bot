namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class OrderDetailEntity
{
    public long OrderCode { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public OrderEntity Order { get; set; } = null!;
    public ProductEntity Product { get; set; } = null!;
}
