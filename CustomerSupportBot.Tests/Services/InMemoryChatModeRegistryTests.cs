// Tests/Services/InMemoryChatModeRegistryTests.cs
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services;

public class InMemoryChatModeRegistryTests
{
    private readonly InMemoryChatModeRegistry _reg = new(NullLogger<InMemoryChatModeRegistry>.Instance);

    [Fact]
    public void GetMode_Default_Bot()
    {
        _reg.GetMode("s1").Should().Be(ChatMode.Bot);
    }

    [Fact]
    public void TakeOver_SwitchesToHuman()
    {
        _reg.TakeOver("s1", "agent42").Should().BeTrue();
        _reg.GetMode("s1").Should().Be(ChatMode.Human);
        _reg.GetState("s1")!.HumanAgent.Should().Be("agent42");
    }

    [Fact]
    public void TakeOver_BlankSession_False()
    {
        _reg.TakeOver("", "x").Should().BeFalse();
    }

    [Fact]
    public void Release_RetursToBot()
    {
        _reg.TakeOver("s1", "a");
        _reg.Release("s1").Should().BeTrue();
        _reg.GetMode("s1").Should().Be(ChatMode.Bot);
    }

    [Fact]
    public void Release_NotInHuman_False()
    {
        _reg.Release("unknown").Should().BeFalse();
    }

    [Fact]
    public void GetActive_OnlyHumans()
    {
        _reg.TakeOver("s1", "a");
        _reg.TakeOver("s2", "b");
        _reg.Release("s2");
        var active = _reg.GetActive();
        active.Should().ContainSingle();
        active[0].SessionId.Should().Be("s1");
    }

    [Fact]
    public void TakeOver_FiresModeChanged()
    {
        ChatSessionState? captured = null;
        _reg.ModeChanged += (_, s) => captured = s;
        _reg.TakeOver("s1", "a");
        captured.Should().NotBeNull();
        captured!.Mode.Should().Be(ChatMode.Human);
    }
}
