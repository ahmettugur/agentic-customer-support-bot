// Bildirim defteri — gerçek Postgres: aynı anahtarı yalnızca bir pod talep eder.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresNotificationLedgerTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentClaims_OnlyOneWins()
    {
        var key = $"approval-result:{Guid.NewGuid():N}";
        var pods = Enumerable.Range(0, 8).Select(_ => new PostgresNotificationLedger(fixture.DbFactory)).ToList();

        var results = await Task.WhenAll(pods.Select(p => p.TryClaimAsync(key, Ct)));

        results.Count(won => won).Should().Be(1);
    }

    [Fact]
    public async Task Release_AllowsTheNextClaim()
    {
        var key = $"approval-result:{Guid.NewGuid():N}";
        var ledger = new PostgresNotificationLedger(fixture.DbFactory);

        (await ledger.TryClaimAsync(key, Ct)).Should().BeTrue();
        (await ledger.TryClaimAsync(key, Ct)).Should().BeFalse();
        await ledger.ReleaseAsync(key, Ct);
        (await ledger.TryClaimAsync(key, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CustomerEmail_IsReadFromTheCatalogRecord()
    {
        var repo = new CustomerRepository(fixture.DbFactory);
        await using var ctx = fixture.DbFactory.CreateDbContext();
        var email = $"musteri-{Guid.NewGuid():N}@example.com";
        var withEmail = new CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog.CustomerEntity
        {
            FullName = "Test Müşteri", Email = email
        };
        ctx.Customers.Add(withEmail);
        await ctx.SaveChangesAsync(Ct);

        (await repo.GetEmailAsync(withEmail.Id, Ct)).Should().Be(email);
        (await repo.GetEmailAsync(-1, Ct)).Should().BeNull();
    }
}
