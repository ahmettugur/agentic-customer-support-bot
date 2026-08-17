namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class CustomerEntity
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
