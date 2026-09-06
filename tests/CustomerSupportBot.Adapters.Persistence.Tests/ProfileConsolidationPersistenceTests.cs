using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class ProfileConsolidationPersistenceTests(PostgresCatalogFixture fixture)
{
    [Fact]
    public async Task PartialConsolidation_PreservesDurableFieldsEvenWhenCacheIsStale()
    {
        var id = Guid.NewGuid().ToString();
        var store = new PostgresCustomerProfileStore(fixture.DbFactory, new InMemoryMessageBusHub().CreateNode(),
            NullLogger<PostgresCustomerProfileStore>.Instance);
        store.Upsert(new CustomerProfile { CustomerId = id, TotalTurns = 1 });
        await using var db = await fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.CustomerProfiles.Where(p => p.CustomerId == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.TotalTurns, 3).SetProperty(p => p.AdminNote, "new note"), TestContext.Current.CancellationToken);
        var profile = await store.UpdateConsolidationAsync(id, "summary", "formal", [], TestContext.Current.CancellationToken);
        profile!.TotalTurns.Should().Be(3);
        profile.AdminNote.Should().Be("new note");
        profile.Summary.Should().Be("summary");
        var saved = await db.CustomerProfiles.AsNoTracking().SingleAsync(p => p.CustomerId == id);
        saved.TotalTurns.Should().Be(3);
        saved.AdminNote.Should().Be("new note");
    }
}
