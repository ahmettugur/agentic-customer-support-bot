// Tests/Services/InMemoryChatModeRegistryTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryChatModeRegistryTests
{
    private readonly InMemoryChatModeRegistry _reg = new(NullLogger<InMemoryChatModeRegistry>.Instance);

    [Fact]
    public void GetMode_Default_Bot()
    {
        _reg.GetMode("s1").Should().Be(ChatMode.Bot);
    }

    [Fact]
    public async Task TakeOver_SwitchesToHuman()
    {
        (await _reg.TakeOverAsync("s1", "agent42")).Should().BeTrue();
        _reg.GetMode("s1").Should().Be(ChatMode.Human);
        _reg.GetState("s1")!.HumanAgent.Should().Be("agent42");
    }

    [Fact]
    public async Task TakeOver_BlankSession_False()
    {
        (await _reg.TakeOverAsync("", "x")).Should().BeFalse();
    }

    [Fact]
    public async Task Release_RetursToBot()
    {
        await _reg.TakeOverAsync("s1", "a");
        (await _reg.ReleaseAsync("s1")).Should().BeTrue();
        _reg.GetMode("s1").Should().Be(ChatMode.Bot);
    }

    [Fact]
    public async Task Release_NotInHuman_False()
    {
        (await _reg.ReleaseAsync("unknown")).Should().BeFalse();
    }

    [Fact]
    public async Task GetActive_OnlyHumans()
    {
        await _reg.TakeOverAsync("s1", "a");
        await _reg.TakeOverAsync("s2", "b");
        await _reg.ReleaseAsync("s2");
        var active = _reg.GetActive();
        active.Should().ContainSingle();
        active[0].SessionId.Should().Be("s1");
    }

    [Fact]
    public async Task TakeOver_FiresModeChanged()
    {
        ChatSessionState? captured = null;
        _reg.ModeChanged += (_, s) => captured = s;
        await _reg.TakeOverAsync("s1", "a");
        captured.Should().NotBeNull();
        captured!.Mode.Should().Be(ChatMode.Human);
    }
}
