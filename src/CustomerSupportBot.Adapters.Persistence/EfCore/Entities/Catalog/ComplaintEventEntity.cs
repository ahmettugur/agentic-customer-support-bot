namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class ComplaintEventEntity
{
    public long Id { get; set; }
    public long ComplaintCode { get; set; }
    public DateTime At { get; set; }
    public string Actor { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? AssignedAgentId { get; set; }
    public string? Note { get; set; }
}
