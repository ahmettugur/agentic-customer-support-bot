using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryChatBridgeTests
{
    private readonly InMemoryChatBridge _bridge = new(NullLogger<InMemoryChatBridge>.Instance);

    [Fact]
    public async Task PublishUserMessage_AppendsToHistory()
    {
        await _bridge.PublishUserMessageAsync("s1", "merhaba");
        var history = await _bridge.GetHistoryAsync("s1");
        history.Should().ContainSingle();
        history[0].Sender.Should().Be(ChatBridgeSender.User);
        history[0].Text.Should().Be("merhaba");
    }

    [Fact]
    public async Task PublishAdminMessage_AppendsWithHumanAgent()
    {
        await _bridge.PublishAdminMessageAsync("s1", "Ali", "destek geldi");
        var history = await _bridge.GetHistoryAsync("s1");
        history[0].Sender.Should().Be(ChatBridgeSender.Admin);
        history[0].HumanAgent.Should().Be("Ali");
    }

    [Fact]
    public async Task PublishSystemMessage_AppendsToHistory()
    {
        await _bridge.PublishSystemMessageAsync("s1", "info");
        (await _bridge.GetHistoryAsync("s1")).Should().HaveCount(1)
            .And.ContainSingle(m => m.Sender == ChatBridgeSender.System);
    }

    [Fact]
    public async Task PublishBotMessage_AppendsToHistory()
    {
        await _bridge.PublishBotMessageAsync("s1", "merhaba ben bot");
        (await _bridge.GetHistoryAsync("s1")).Should().ContainSingle(m => m.Sender == ChatBridgeSender.Bot);
    }

    [Fact]
    public async Task PublishBotTyping_DoesNotAppendToHistory()
    {
        _bridge.PublishBotTyping("s1", on: true);
        (await _bridge.GetHistoryAsync("s1")).Should().BeEmpty();
    }

    [Fact]
    public async Task RecordBotExchange_BothMessagesAppended()
    {
        await _bridge.RecordBotExchangeAsync("s1", "soru", "cevap");
        (await _bridge.GetHistoryAsync("s1")).Should().HaveCount(2);
    }

    [Fact]
    public async Task RecordBotExchange_SkipsBlankParts()
    {
        await _bridge.RecordBotExchangeAsync("s1", "  ", "cevap");
        (await _bridge.GetHistoryAsync("s1")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetHistory_TakeLimits()
    {
        for (var i = 0; i < 10; i++)
            await _bridge.PublishUserMessageAsync("s1", $"m{i}");
        (await _bridge.GetHistoryAsync("s1", take: 3)).Should().HaveCount(3);
    }

    [Fact]
    public async Task GetHistory_UnknownSession_Empty()
    {
        (await _bridge.GetHistoryAsync("missing")).Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_ClearsHistory()
    {
        await _bridge.PublishUserMessageAsync("s1", "x");
        _bridge.Reset("s1");
        (await _bridge.GetHistoryAsync("s1")).Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeToUser_ReceivesAdminMessage()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = Task.Run(async () =>
        {
            await foreach (var m in _bridge.SubscribeToUserAsync("s1", cts.Token))
            {
                return m;
            }
            return null;
        }, cts.Token);

        // Aboneliğin oluşması için kısa bekleme
        await Task.Delay(100, cts.Token);
        await _bridge.PublishAdminMessageAsync("s1", "Ali", "merhaba");

        var msg = await task;
        msg.Should().NotBeNull();
        msg.Sender.Should().Be(ChatBridgeSender.Admin);
    }

    [Fact]
    public async Task SubscribeToAdmin_ReceivesUserMessage()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = Task.Run(async () =>
        {
            await foreach (var m in _bridge.SubscribeToAdminAsync("s1", cts.Token))
            {
                return m;
            }
            return null;
        }, cts.Token);

        await Task.Delay(100, cts.Token);
        await _bridge.PublishUserMessageAsync("s1", "merhaba");

        var msg = await task;
        msg.Should().NotBeNull();
        msg.Sender.Should().Be(ChatBridgeSender.User);
    }
}
