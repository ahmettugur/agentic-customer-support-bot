// Chat bridge'in her turda çalışan yazmaları çağıranı BLOKLUYOR mu?
//
// RecordBotExchange her bot turunun sonunda (yazılı ve sesli), PublishUserMessage insan
// modundaki her müşteri mesajında çalışır; ikisi de DB'ye yazar. Eskiden senkron arayüzün
// arkasında GetAwaiter().GetResult() ile yazıyorlardı — DB yavaşladığında her tur bir
// thread-pool thread'ini rehin tutuyordu. Ölçüm PostgresReasoningTraceStoreAsyncTests ile aynı.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresChatBridgeAsyncTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresChatBridgeAsyncTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static string NewSessionId() => $"bridge-async-{Guid.NewGuid():N}";

    [Fact]
    public async Task RecordBotExchangeAsync_DoesNotBlockTheCallerWhileTheDbIsSlow()
    {
        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var bridge = new PostgresChatBridge(gate, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

        var promptly = await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate,
            () => bridge.RecordBotExchangeAsync(NewSessionId(), "siparişim nerede", "kargoda"));

        promptly.Should().BeTrue("DB yazması beklenirken çağıranın thread'i bloklanmamalı");
    }

    [Fact]
    public async Task PublishUserMessageAsync_DoesNotBlockTheCallerWhileTheDbIsSlow()
    {
        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var bridge = new PostgresChatBridge(gate, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

        var promptly = await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate,
            () => bridge.PublishUserMessageAsync(NewSessionId(), "temsilciyle görüşmek istiyorum"));

        promptly.Should().BeTrue("DB yazması beklenirken çağıranın thread'i bloklanmamalı");
    }

    /// <summary>
    /// Oturum başına hydrate açılışta ısıtılamaz (hangi oturumların okunacağı bilinmez); bu
    /// yüzden geçmişin ilk okuması da asenkron olmalı.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_DoesNotBlockTheCallerWhileTheDbIsSlow()
    {
        var gate = new GatedDbContextFactory(_fixture.DbFactory);
        var bridge = new PostgresChatBridge(gate, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

        var promptly = await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate,
            () => bridge.GetHistoryAsync(NewSessionId()));

        promptly.Should().BeTrue("ilk geçmiş okuması DB'yi beklerken çağıranın thread'ini bloklamamalı");
    }

    [Fact]
    public async Task RecordBotExchangeAsync_IsPersisted()
    {
        var sessionId = NewSessionId();
        var writer = new PostgresChatBridge(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

        await writer.RecordBotExchangeAsync(sessionId, "siparişim nerede", "kargoda");

        var reader = new PostgresChatBridge(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);
        (await reader.GetHistoryAsync(sessionId)).Select(m => (m.Sender, m.Text)).Should().Equal(
            (ChatBridgeSender.User, "siparişim nerede"),
            (ChatBridgeSender.Bot, "kargoda"));
    }
}
