// Trace yazmaları çağıranın thread'ini BLOKLUYOR mu?
//
// StartTrace/Complete her sohbet turunda çalışır ve ikisi de DB'ye yazar. Eskiden ikisi de
// senkron arayüzün arkasında GetAwaiter().GetResult() ile yazıyordu: DB yavaşladığında her
// tur bir thread-pool thread'ini boşuna rehin tutuyor, yük altında thread-pool açlığına
// (tüm isteklerin birden yavaşlaması) zemin hazırlıyordu.
//
// Ölçüm: ilk DB bağlantısını test açana kadar bekleten bir factory. Gerçekten asenkron bir
// çağrı, DB beklenirken bile TAMAMLANMAMIŞ bir Task ile hemen döner; senkron-üstü-asenkron
// bir çağrı ise kapı açılana kadar çağıranın thread'inde asılı kalır.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresReasoningTraceStoreAsyncTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresReasoningTraceStoreAsyncTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task StartTraceAsync_DoesNotBlockTheCallerWhileTheDbIsSlow()
    {
        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var store = new PostgresReasoningTraceStore(gate, new NoopMessageBus(), NullLogger<PostgresReasoningTraceStore>.Instance);

        var promptly = await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate,
            () => store.StartTraceAsync("oturum", "siparişim nerede"));

        promptly.Should().BeTrue("DB yazması beklenirken çağıranın thread'i bloklanmamalı");
    }

    [Fact]
    public async Task CompleteAsync_PersistsTheFinalState()
    {
        var store = new PostgresReasoningTraceStore(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresReasoningTraceStore>.Instance);
        var trace = await store.StartTraceAsync("oturum", "siparişim nerede");

        await store.CompleteAsync(trace.TraceId, terminationReason: "completed", finalResponse: "kargoda");

        var fromDb = new PostgresReasoningTraceStore(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresReasoningTraceStore>.Instance)
            .Get(trace.TraceId);
        fromDb.Should().NotBeNull();
        fromDb!.CompletedAt.Should().NotBeNull();
        fromDb.TerminationReason.Should().Be("completed");
        fromDb.FinalResponse.Should().Be("kargoda");
    }
}
