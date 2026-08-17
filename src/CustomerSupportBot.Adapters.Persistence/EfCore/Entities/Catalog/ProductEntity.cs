namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class ProductEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int CategoryId { get; set; }
    public CategoryEntity Category { get; set; } = null!;
    public ICollection<OrderDetailEntity> OrderDetails { get; set; } = [];
}
