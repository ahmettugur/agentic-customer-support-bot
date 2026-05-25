using CustomerSupportBot.Adapters.Persistence.EfCore;

namespace CustomerSupportBot.Api.Tests.Infrastructure.Persistence;

public class PersistenceOptionsTests
{
    [Fact]
    public void Defaults_ProviderIsPostgres()
    {
        var o = new PersistenceOptions();
        o.Provider.Should().Be(PersistenceProvider.Postgres);
        PersistenceOptions.SectionName.Should().Be("Persistence");
    }

    [Fact]
    public void EnumHasPostgres()
    {
        Enum.GetNames<PersistenceProvider>()
            .Should().Contain("Postgres");
    }
}
