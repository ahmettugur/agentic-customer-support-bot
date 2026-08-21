// ChatBridge aboneliklerinin KAYITTAN ÇIKARILMASI.
//
// Her SubscribeToAdminAsync/SubscribeToUserAsync çağrısı session başına tutulan bir kümeye
// bir Channel ekliyordu ama abone ayrıldığında (WebSocket/SSE kapandığında) hiçbir zaman
// çıkarılmıyordu — yalnızca Writer.TryComplete() çağrılıyordu. Sık bağlanıp kopan bir oturumda
// (sayfa yenileme, WebSocket yeniden bağlanma) bu, tamamlanmış-ama-hâlâ-tutulan kanalların
// process ömrü boyunca birikmesi demekti: bellek sınırsız büyür ve her Broadcast çağrısı,
// artık kimsenin okumadığı bu kanalları da tarayarak session'ın yaşı ilerledikçe yavaşlardı.
//
// Kayıt kümesi private olduğu için reflection ile okunuyor — dış API'de "kaç abone var"
// sorusuna cevap veren bir uç yok, doğru olan da bu (bu bir uygulama detayı).

using System.Collections;
using System.Reflection;
using System.Threading.Channels;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class ChatBridgeSubscriptionCleanupTests
{
    private readonly PostgresCatalogFixture _fixture;

    public ChatBridgeSubscriptionCleanupTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static PostgresChatBridge NewBridge(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

    /// <summary>Session başına abone kümesindeki eleman sayısı — reflection ile.</summary>
    private static int SubscriberCount(PostgresChatBridge bridge, string fieldName, string sessionId)
    {
        var field = typeof(PostgresChatBridge).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Alan bulunamadı: {fieldName}");
        var registry = (IDictionary)field.GetValue(bridge)!;
        if (!registry.Contains(sessionId)) return 0;
        var set = (IDictionary)registry[sessionId]!;
        return set.Count;
    }

    /// <summary>
    /// ASIL BULGU. Abonelik iptal edilip yield döngüsü finally'sinden çıktıktan sonra, kanal
    /// artık session'ın abone kümesinde OLMAMALI.
    /// </summary>
    [Fact]
    public async Task Unsubscribing_RemovesTheChannelFromTheRegistry_NotJustCompletesIt()
    {
        var sessionId = $"cleanup-{Guid.NewGuid():N}";
        var bridge = NewBridge(_fixture.DbFactory);

        using var cts = new CancellationTokenSource();
        var enumerator = bridge.SubscribeToUserAsync(sessionId, cts.Token).GetAsyncEnumerator(cts.Token);

        // Aboneliği gerçekten BAŞLAT — kayıt CreateAndRegister içinde, ilk MoveNextAsync
        // çağrılmadan da olur (Subscribe metodunun ilk satırlarında), ama enumerator'ı
        // en az bir kez ilerletmek gerçek kullanım şeklini yansıtır.
        var moveNextTask = enumerator.MoveNextAsync().AsTask();

        // Kayıt olduğunu doğrula.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        SubscriberCount(bridge, "_toUser", sessionId).Should().Be(1,
            "abonelik başlatıldığında kanal kayda girmeli");

        // Ayrıl — WebSocket/SSE bağlantısının kapanmasının karşılığı.
        await cts.CancelAsync();
        try { await moveNextTask; } catch (OperationCanceledException) { }
        try { await enumerator.DisposeAsync(); } catch (OperationCanceledException) { }

        SubscriberCount(bridge, "_toUser", sessionId).Should().Be(0,
            "abonelik sona erdiğinde kanal kayıttan ÇIKARILMALI, yalnızca tamamlanmış işaretlenmemeli — " +
            "aksi hâlde sık bağlanıp kopan bir oturumda bu küme process ömrü boyunca büyür");
    }

    /// <summary>Karşı yön: aynı session'a birden fazla eşzamanlı abone olabilmeli, biri ayrılınca diğeri etkilenmemeli.</summary>
    [Fact]
    public async Task OneSubscriberLeaving_DoesNotAffectAnother()
    {
        var sessionId = $"cleanup-multi-{Guid.NewGuid():N}";
        var bridge = NewBridge(_fixture.DbFactory);

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();
        var enumA = bridge.SubscribeToUserAsync(sessionId, ctsA.Token).GetAsyncEnumerator(ctsA.Token);
        var enumB = bridge.SubscribeToUserAsync(sessionId, ctsB.Token).GetAsyncEnumerator(ctsB.Token);

        var moveA = enumA.MoveNextAsync().AsTask();
        var moveB = enumB.MoveNextAsync().AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        SubscriberCount(bridge, "_toUser", sessionId).Should().Be(2);

        await ctsA.CancelAsync();
        try { await moveA; } catch (OperationCanceledException) { }
        try { await enumA.DisposeAsync(); } catch (OperationCanceledException) { }

        SubscriberCount(bridge, "_toUser", sessionId).Should().Be(1,
            "yalnızca ayrılan abone kayıttan çıkmalı");

        await ctsB.CancelAsync();
        try { await moveB; } catch (OperationCanceledException) { }
        try { await enumB.DisposeAsync(); } catch (OperationCanceledException) { }
    }
}
