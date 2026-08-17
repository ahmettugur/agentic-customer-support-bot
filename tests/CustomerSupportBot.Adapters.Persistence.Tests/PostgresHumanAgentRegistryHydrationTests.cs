// Tests/Services/PostgresHumanAgentRegistryHydrationTests.cs
//
// Aynı hydration-flag regresyonu (bkz. PostgresSessionManagerHydrationTests) burada da
// vardı: _hydrated = true, try/catch'in DIŞINDA set ediliyordu — hydrate DB hatasıyla
// başarısız olsa bile flag "başarılı" işaretleniyor, registry process ömrü boyunca boş
// kalıyordu.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresHumanAgentRegistryHydrationTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresHumanAgentRegistryHydrationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static PostgresHumanAgentRegistry NewRegistry(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, new NoopMessageBus(), NullLogger<PostgresHumanAgentRegistry>.Instance);

    private async Task InsertAgentRowAsync(string id)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        ctx.HumanAgents.Add(new HumanAgentEntity
        {
            Id = id,
            DisplayName = "Test Temsilci",
            Email = $"{id}@example.com",
            IsActive = true,
            MaxConcurrentLoad = 5,
            CurrentLoad = 0,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetAll_TransientHydrationFailure_RetriesOnNextCall()
    {
        var agentId = $"a{Guid.NewGuid():N}"[..24];
        await InsertAgentRowAsync(agentId);

        var flaky = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: 1);
        var registry = NewRegistry(flaky);

        var firstAttempt = registry.GetAll();
        firstAttempt.Should().BeEmpty(
            "ilk deneme DB hatasıyla başarısız olmalı — cache boş kalmalı");

        var secondAttempt = registry.GetAll();
        secondAttempt.Should().Contain(a => a.Id == agentId,
            "_hydrated true'ya çekilmediyse ikinci deneme DB'yi hiç sorgulamaz ve registry sonsuza dek boş kalırdı");
    }

    [Fact]
    public void Create_PublishesAgent_VisibleOnOtherPodWithoutDbAccess()
    {
        // Bu registry MessageBus'a bağlı DEĞİLDİ — çoklu pod dağıtımında pod A'nın
        // eklediği temsilci pod B'nin routing kararlarında hiç görünmüyordu.
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresHumanAgentRegistry(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresHumanAgentRegistry>.Instance);

        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresHumanAgentRegistry(
            neverReachesDb, hub.CreateNode(), NullLogger<PostgresHumanAgentRegistry>.Instance);

        var created = writer.Create(new HumanAgent { DisplayName = "Ayşe", MaxConcurrentLoad = 3 });

        var seenByReader = reader.Get(created.Id);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu kayıt yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.DisplayName.Should().Be("Ayşe");
    }

    [Fact]
    public void Delete_PublishesRemoval_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresHumanAgentRegistry(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresHumanAgentRegistry>.Instance);
        var reader = new PostgresHumanAgentRegistry(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresHumanAgentRegistry>.Instance);

        var created = writer.Create(new HumanAgent { DisplayName = "Silinecek" });
        reader.Get(created.Id).Should().NotBeNull(); // reader kendi DB'sinden hydrate eder, cache'e girer

        writer.Delete(created.Id);

        reader.Get(created.Id).Should().BeNull(
            "silme Redis üzerinden yayınlanmalı, reader'ın cache'inde kayıt kalmamalı");
    }
}
