// Tests/Services/InMemorySessionManagerTests.cs

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services;

public class InMemorySessionManagerTests
{
    private static readonly IAppDistributedLock _lock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
    private readonly InMemorySessionManager _mgr = new(_lock);

    [Fact]
    public void GetOrCreateSession_NewId_CreatesSession()
    {
        var s = _mgr.GetOrCreate("s1");
        s.Should().NotBeNull();
        s.SessionId.Should().Be("s1");
        s.State.Should().NotBeNull();
    }

    [Fact]
    public void GetOrCreateSession_ExistingId_ReturnsSame()
    {
        var s1 = _mgr.GetOrCreate("s1");
        var s2 = _mgr.GetOrCreate("s1");
        s1.Should().BeSameAs(s2);
    }

    [Fact]
    public void GetOrCreateSession_NullId_GeneratesNewId()
    {
        var s = _mgr.GetOrCreate(null);
        s.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSession_Unknown_ReturnsNull()
    {
        _mgr.Get("unknown").Should().BeNull();
    }

    [Fact]
    public void AddExchange_BuildsHistory()
    {
        _mgr.AddExchange("s1", "merhaba", "selam");
        _mgr.AddExchange("s1", "yard�m", "tabii");

        var history = _mgr.GetHistory("s1");
        history.Should().HaveCount(4);
        history[0].Text.Should().Be("merhaba");
        history[1].Text.Should().Be("selam");
    }

    [Fact]
    public void GetHistory_UnknownSession_EmptyList()
    {
        _mgr.GetHistory("unknown").Should().BeEmpty();
    }

    [Fact]
    public void ExtractAndUpdateState_CapturesCustomerId()
    {
        _mgr.AddExchange("s1", "ben CUST-1990 m��teriyim", "merhaba");
        var s = _mgr.Get("s1");
        s!.State.CustomerId.Should().Be("CUST-1990");
    }

    [Fact]
    public void ExtractAndUpdateState_CapturesOrderId()
    {
        _mgr.AddExchange("s1", "ORD-1 nerede?", "sipari�iniz yolda");
        var s = _mgr.Get("s1");
        s!.State.CollectedInfo.Should().ContainKey("LastMentionedOrderId");
    }

    [Fact]
    public void AddExchange_IncrementsTurnCount()
    {
        _mgr.AddExchange("s1", "msg1", "r1");
        _mgr.AddExchange("s1", "msg2", "r2");
        _mgr.Get("s1")!.State.TurnCount.Should().Be(2);
    }

    [Fact]
    public void ClearSession_RemovesData()
    {
        _mgr.AddExchange("s1", "x", "y");
        _mgr.ClearSession("s1");
        _mgr.Get("s1").Should().BeNull();
        _mgr.GetHistory("s1").Should().BeEmpty();
    }

    [Fact]
    public void AppendAssistantMessage_AppendsToHistory()
    {
        _mgr.GetOrCreate("s1");
        _mgr.AppendAssistantMessage("s1", "agent yan�t�");
        var h = _mgr.GetHistory("s1");
        h.Should().HaveCount(1);
        h[0].Text.Should().Be("agent yan�t�");
    }

    [Fact]
    public void AppendAssistantMessage_FillsEmptyPlaceholder()
    {
        _mgr.AddExchange("s1", "user", "");
        _mgr.AppendAssistantMessage("s1", "real reply");
        var h = _mgr.GetHistory("s1");
        h.Should().HaveCount(2);
        h[1].Text.Should().Be("real reply");
    }

    [Fact]
    public void UpdateSession_UpdatesLastActivity()
    {
        var s = _mgr.GetOrCreate("s1");
        var before = s.LastActivity;
        Thread.Sleep(5);
        _mgr.Update(s);
        s.LastActivity.Should().BeAfter(before);
    }
}
