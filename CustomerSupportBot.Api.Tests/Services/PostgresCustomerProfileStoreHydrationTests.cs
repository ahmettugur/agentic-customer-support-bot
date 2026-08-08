// Tests/Services/PostgresCustomerProfileStoreHydrationTests.cs
//
// PostgresCustomerProfileStore, diğer Postgres adaptörleri gibi hiç Postgres'e karşı test
// edilmiyordu ve MessageBus'a bağlı DEĞİLDİ — çoklu pod dağıtımında pod A'da güncellenen
// bir müşteri profili (dil/ton tercihi, ilgi alanları) pod B'de hiç görünmüyordu.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

[Collection("PostgresCatalog")]
public class PostgresCustomerProfileStoreHydrationTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresCustomerProfileStoreHydrationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static PostgresCustomerProfileStore NewStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, new NoopMessageBus(), NullLogger<PostgresCustomerProfileStore>.Instance);

    [Fact]
    public void Get_TransientHydrationFailure_RetriesOnNextCall()
    {
        var writer = NewStore(_fixture.DbFactory);
        var customerId = $"cust-{Guid.NewGuid():N}"[..16];
        writer.Upsert(new CustomerProfile { CustomerId = customerId, PreferredTone = "concise" });

        var flaky = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: 1);
        var reader = NewStore(flaky);

        var firstAttempt = reader.Get(customerId);
        firstAttempt.Should().BeNull(
            "ilk deneme DB hatasıyla başarısız olmalı — cache boş kalmalı");

        var secondAttempt = reader.Get(customerId);
        secondAttempt.Should().NotBeNull(
            "flag geri alınmadıysa ikinci deneme DB'yi hiç sorgulamaz ve profil sonsuza dek görünmez kalırdı");
        secondAttempt!.PreferredTone.Should().Be("concise");
    }

    [Fact]
    public void Upsert_PublishesProfile_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresCustomerProfileStore(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresCustomerProfileStore>.Instance);

        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresCustomerProfileStore(
            neverReachesDb, hub.CreateNode(), NullLogger<PostgresCustomerProfileStore>.Instance);

        var customerId = $"cust-{Guid.NewGuid():N}"[..16];
        writer.Upsert(new CustomerProfile { CustomerId = customerId, PreferredTone = "formal", Summary = "VIP müşteri" });

        var seenByReader = reader.Get(customerId);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu profil yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.PreferredTone.Should().Be("formal");
        seenByReader.Summary.Should().Be("VIP müşteri");
    }

    [Fact]
    public void Delete_PublishesRemoval_VisibleOnOtherPodWithoutFurtherDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresCustomerProfileStore(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresCustomerProfileStore>.Instance);
        var reader = new PostgresCustomerProfileStore(
            _fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresCustomerProfileStore>.Instance);

        var customerId = $"cust-{Guid.NewGuid():N}"[..16];
        writer.Upsert(new CustomerProfile { CustomerId = customerId });
        reader.Get(customerId).Should().NotBeNull(); // reader kendi DB'sinden hydrate eder

        writer.Delete(customerId);

        reader.Get(customerId).Should().BeNull(
            "silme Redis üzerinden yayınlanmalı, reader'ın cache'inde kayıt kalmamalı");
    }
}
