// Sinyal yalnızca hedef tarafa gider ve geçmişe yazılmaz; döküm satırı yalnız temsilciye gider ve geçmişte kalır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryChatBridgeVoiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<ChatBridgeMessage>> CollectAsync(IAsyncEnumerable<ChatBridgeMessage> stream, int count)
    {
        var list = new List<ChatBridgeMessage>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { await foreach (var m in stream.WithCancellation(cts.Token)) { list.Add(m); if (list.Count == count) break; } }
        catch (OperationCanceledException) { }
        return list;
    }

    [Fact]
    public async Task VoiceSignal_GoesOnlyToTarget_AndIsNotPersisted()
    {
        var bridge = new InMemoryChatBridge(NullLogger<InMemoryChatBridge>.Instance);
        var toUser = CollectAsync(bridge.SubscribeToUserAsync("s1", Ct), 1);
        var toAdmin = CollectAsync(bridge.SubscribeToAdminAsync("s1", Ct), 1);
        await Task.Delay(50, Ct);

        bridge.PublishVoiceSignal("s1", toCustomer: true, """{"callId":"c1","type":"ring"}""");

        (await toUser).Should().ContainSingle(m => m.Sender == ChatBridgeSender.VoiceSignal && m.Text.Contains("ring"));
        (await toAdmin).Should().BeEmpty();
        (await bridge.GetHistoryAsync("s1")).Should().BeEmpty();
    }

    [Fact]
    public async Task VoiceTranscript_GoesToAdminOnly_WithMeta_AndIsPersisted()
    {
        var bridge = new InMemoryChatBridge(NullLogger<InMemoryChatBridge>.Instance);
        var toUser = CollectAsync(bridge.SubscribeToUserAsync("s1", Ct), 1);
        var toAdmin = CollectAsync(bridge.SubscribeToAdminAsync("s1", Ct), 1);
        await Task.Delay(50, Ct);

        await bridge.PublishVoiceTranscriptAsync("s1", "c1", "customer", 20000, "Müşteri: Kargom gelmedi.");

        var admin = (await toAdmin).Single();
        admin.VoiceCallId.Should().Be("c1");
        admin.VoiceTrack.Should().Be("customer");
        admin.OffsetMs.Should().Be(20000);
        (await toUser).Should().BeEmpty();
        (await bridge.GetHistoryAsync("s1")).Should().ContainSingle(m => m.VoiceCallId == "c1");
    }
}
