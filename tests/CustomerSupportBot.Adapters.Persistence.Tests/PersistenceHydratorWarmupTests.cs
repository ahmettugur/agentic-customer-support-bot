// Hibrit cache'ler açılışta ISITILIYOR mu?
//
// Bu adaptörlerin okuma uçları senkron port arayüzlerinin arkasındadır; cache boşsa İLK
// okuma hydrate'i GetAwaiter().GetResult() ile bekler — yani o isteğin thread'i DB okuması
// boyunca bloklanır. PersistenceHydrator açılışta cache'leri asenkron doldurduğunda ilk okuma
// DB'ye hiç gitmez; senkron yol yalnızca ısıtma başarısız olursa devreye giren bir yedektir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Improvement;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PersistenceHydratorWarmupTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PersistenceHydratorWarmupTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private sealed class CountingDbContextFactory(IDbContextFactory<CustomerSupportDbContext> inner)
        : IDbContextFactory<CustomerSupportDbContext>
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public CustomerSupportDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _count);
            return inner.CreateDbContext();
        }

        public Task<CustomerSupportDbContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return inner.CreateDbContextAsync(ct);
        }
    }

    [Fact]
    public async Task AfterStartup_TheFirstReadOfEveryHybridCache_DoesNotTouchTheDb()
    {
        var db = new CountingDbContextFactory(_fixture.DbFactory);
        var bus = new NoopMessageBus();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageBusPort>(bus);
        services.AddSingleton<IEscalationSink>(new PostgresEscalationSink(db, bus, NullLogger<PostgresEscalationSink>.Instance));
        services.AddSingleton<ISlaEventSink>(new PostgresSlaEventSink(db, bus, NullLogger<PostgresSlaEventSink>.Instance));
        services.AddSingleton<ILessonStore>(new PostgresLessonStore(db, bus, NullLogger<PostgresLessonStore>.Instance));
        services.AddSingleton<IRatingStore>(new PostgresRatingStore(db, bus, NullLogger<PostgresRatingStore>.Instance));
        services.AddSingleton<ICustomerProfileStore>(new PostgresCustomerProfileStore(db, bus, NullLogger<PostgresCustomerProfileStore>.Instance));
        services.AddSingleton<IChatModeRegistry>(new PostgresChatModeRegistry(db, bus,
            new InMemoryDistributedLock(Options.Create(new RedisOptions())), NullLogger<PostgresChatModeRegistry>.Instance));
        services.AddSingleton<IReasoningTraceStore>(new PostgresReasoningTraceStore(db, bus, NullLogger<PostgresReasoningTraceStore>.Instance));
        using var sp = services.BuildServiceProvider();

        await new PersistenceHydrator(sp, NullLogger<PersistenceHydrator>.Instance)
            .StartAsync(TestContext.Current.CancellationToken);
        var afterStartup = db.Count;

        _ = sp.GetRequiredService<IEscalationSink>().GetOpen();
        _ = sp.GetRequiredService<ISlaEventSink>().GetRecent();
        _ = sp.GetRequiredService<ILessonStore>().GetByStatus(LessonStatus.Proposed);
        _ = sp.GetRequiredService<IRatingStore>().GetRecent();
        _ = sp.GetRequiredService<ICustomerProfileStore>().List();
        _ = sp.GetRequiredService<IChatModeRegistry>().GetActive();
        _ = sp.GetRequiredService<IReasoningTraceStore>().GetRecent();

        db.Count.Should().Be(afterStartup,
            "cache'ler açılışta ısıtıldıysa ilk senkron okumalar DB'ye gitmemeli (ve thread'i bloklamamalı)");
    }
}
