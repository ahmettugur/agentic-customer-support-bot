// Tests/Infrastructure/Persistence/PersistenceOptionsTests.cs

using CustomerSupportBot.Adapters.Persistence.EfCore;

namespace CustomerSupportBot.Api.Tests.Infrastructure.Persistence;

public class PersistenceOptionsTests
{
    [Fact]
    public void Defaults_ProviderIsInMemory()
    {
        var o = new PersistenceOptions();
        o.Provider.Should().Be(PersistenceProvider.InMemory);
        PersistenceOptions.SectionName.Should().Be("Persistence");
    }

    [Fact]
    public void EnumHasInMemoryAndPostgres()
    {
        Enum.GetNames<PersistenceProvider>()
            .Should().Contain(new[] { "InMemory", "Postgres" });
    }
}
