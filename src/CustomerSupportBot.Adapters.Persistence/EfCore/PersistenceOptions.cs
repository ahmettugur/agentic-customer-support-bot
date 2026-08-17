namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public PersistenceProvider Provider { get; set; } = PersistenceProvider.Postgres;
}

public enum PersistenceProvider
{
    Postgres
}
