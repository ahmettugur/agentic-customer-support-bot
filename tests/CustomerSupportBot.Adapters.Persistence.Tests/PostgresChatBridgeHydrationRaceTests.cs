// Chat bridge hydrate'i sürerken gelen İKİNCİ çağıran ne görüyor?
//
// "Hydrate edildi" bayrağı DB okuması BAŞLAMADAN konuyordu: aynı oturuma eşzamanlı gelen
// ikinci çağıran bayrağı görüp hemen devam ediyor, yani okuma sürerken (a) boş bir geçmiş
// döndürüyor, (b) mesajını cache'e ve DB'ye yazıyordu — ardından biten hydrate aynı mesajı
// DB'den bir kez daha ekliyor ve admin paneli mesajı İKİ KEZ gösteriyordu. PostgresSessionManager
// aynı hatayı Lazy<Task> deseniyle çözmüştü (bkz. PostgresSessionManagerHydrationTests).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresChatBridgeHydrationRaceTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresChatBridgeHydrationRaceTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static PostgresChatBridge NewBridge(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

    private static string NewSessionId() => $"bridge-race-{Guid.NewGuid():N}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReaderArrivingDuringHydration_SeesTheStoredHistory_NotAnEmptyList()
    {
        var sessionId = NewSessionId();
        await NewBridge(_fixture.DbFactory).PublishUserMessageAsync(sessionId, "eski mesaj");

        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var reader = NewBridge(gate);

        var first = Task.Run(() => reader.GetHistoryAsync(sessionId), Ct);
        await gate.FirstCallEntered;                       // hydrate DB'de bekliyor
        var second = Task.Run(() => reader.GetHistoryAsync(sessionId), Ct);
        await Task.WhenAny(second, Task.Delay(300, Ct));       // hatalı sürüm burada hemen döner
        gate.OpenGate();

        (await first).Select(m => m.Text).Should().Contain("eski mesaj");
        (await second).Select(m => m.Text).Should().Contain("eski mesaj",
            "hydrate sürerken okuyan ikinci çağıran boş geçmiş görmemeli");
    }

    [Fact]
    public async Task AWriterArrivingDuringHydration_IsNotDuplicatedInTheHistory()
    {
        var sessionId = NewSessionId();
        await NewBridge(_fixture.DbFactory).PublishUserMessageAsync(sessionId, "eski mesaj");

        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var bridge = NewBridge(gate);

        var first = Task.Run(() => bridge.PublishUserMessageAsync(sessionId, "birinci"), Ct);
        await gate.FirstCallEntered;
        var second = Task.Run(() => bridge.PublishUserMessageAsync(sessionId, "ikinci"), Ct);
        await Task.WhenAny(second, Task.Delay(300, Ct));
        gate.OpenGate();
        await Task.WhenAll(first, second);

        (await bridge.GetHistoryAsync(sessionId)).Select(m => m.Text).Should()
            .BeEquivalentTo(["eski mesaj", "birinci", "ikinci"], "her mesaj geçmişte tam bir kez görünmeli");
    }

    /// <summary>
    /// GetHistory listeyi kilitsiz okuyordu, Append ise kilit altında ekleyip kapasite taşınca
    /// RemoveRange yapıyordu. Eşzamanlı okuma, küçülen listeyi indeksle gezerken patlayabilir.
    /// </summary>
    [Fact]
    public async Task InMemory_GetHistoryWhileAppending_NeverThrows()
    {
        var bridge = new InMemoryChatBridge(NullLogger<InMemoryChatBridge>.Instance);
        var sessionId = NewSessionId();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(async () =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
                await bridge.PublishUserMessageAsync(sessionId, $"m{i}");
        }, Ct);
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) _ = await bridge.GetHistoryAsync(sessionId, take: 150);
        }, Ct);

        var act = () => Task.WhenAll(writer, reader);
        await act.Should().NotThrowAsync("eşzamanlı okuma, yazma sırasında listeyi bozuk görmemeli");
    }
}
