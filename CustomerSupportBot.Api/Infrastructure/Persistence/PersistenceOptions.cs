// Infrastructure/Persistence/PersistenceOptions.cs
// "Persistence" bölümünden bind edilir. Provider seçimi:
//   InMemory  → mevcut in-memory store'lar (default)
//   Postgres  → EF Core + Npgsql tabanlı store'lar
// İleride Redis eklendiğinde bu enum genişletilir.

namespace CustomerSupportBot.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public PersistenceProvider Provider { get; set; } = PersistenceProvider.InMemory;
}

public enum PersistenceProvider
{
    InMemory,
    Postgres
}
