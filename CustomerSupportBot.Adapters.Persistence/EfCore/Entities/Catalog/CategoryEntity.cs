namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class CategoryEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<ProductEntity> Products { get; set; } = [];
}
