using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class InMemoryChatBridgeTests
{
    private readonly InMemoryChatBridge _bridge = new(NullLogger<InMemoryChatBridge>.Instance);

    [Fact]
    public void PublishUserMessage_AppendsToHistory()
    {
        _bridge.PublishUserMessage("s1", "merhaba");
        var history = _bridge.GetHistory("s1");
        history.Should().ContainSingle();
        history[0].Sender.Should().Be(ChatBridgeSender.User);
        history[0].Text.Should().Be("merhaba");
    }

    [Fact]
    public void PublishAdminMessage_AppendsWithHumanAgent()
    {
        _bridge.PublishAdminMessage("s1", "Ali", "destek geldi");
        var history = _bridge.GetHistory("s1");
        history[0].Sender.Should().Be(ChatBridgeSender.Admin);
        history[0].HumanAgent.Should().Be("Ali");
    }

    [Fact]
    public void PublishSystemMessage_AppendsToHistory()
    {
        _bridge.PublishSystemMessage("s1", "info");
        _bridge.GetHistory("s1").Should().HaveCount(1)
            .And.ContainSingle(m => m.Sender == ChatBridgeSender.System);
    }

    [Fact]
    public void PublishBotMessage_AppendsToHistory()
    {
        _bridge.PublishBotMessage("s1", "merhaba ben bot");
        _bridge.GetHistory("s1").Should().ContainSingle(m => m.Sender == ChatBridgeSender.Bot);
    }

    [Fact]
    public void PublishBotTyping_DoesNotAppendToHistory()
    {
        _bridge.PublishBotTyping("s1", on: true);
        _bridge.GetHistory("s1").Should().BeEmpty();
    }

    [Fact]
    public void RecordBotExchange_BothMessagesAppended()
    {
        _bridge.RecordBotExchange("s1", "soru", "cevap");
        _bridge.GetHistory("s1").Should().HaveCount(2);
    }

    [Fact]
    public void RecordBotExchange_SkipsBlankParts()
    {
        _bridge.RecordBotExchange("s1", "  ", "cevap");
        _bridge.GetHistory("s1").Should().ContainSingle();
    }

    [Fact]
    public void GetHistory_TakeLimits()
    {
        for (var i = 0; i < 10; i++)
            _bridge.PublishUserMessage("s1", $"m{i}");
        _bridge.GetHistory("s1", take: 3).Should().HaveCount(3);
    }

    [Fact]
    public void GetHistory_UnknownSession_Empty()
    {
        _bridge.GetHistory("missing").Should().BeEmpty();
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        _bridge.PublishUserMessage("s1", "x");
        _bridge.Reset("s1");
        _bridge.GetHistory("s1").Should().BeEmpty();
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
        _bridge.PublishAdminMessage("s1", "Ali", "merhaba");

        var msg = await task;
        msg.Should().NotBeNull();
        msg!.Sender.Should().Be(ChatBridgeSender.Admin);
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
        _bridge.PublishUserMessage("s1", "merhaba");

        var msg = await task;
        msg.Should().NotBeNull();
        msg!.Sender.Should().Be(ChatBridgeSender.User);
    }
}
