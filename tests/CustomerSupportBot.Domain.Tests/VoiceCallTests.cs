// Sesli görüşme durum makinesi: rıza olmadan görüşme olmaz, bitmiş görüşme yeniden açılmaz.

using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Domain.Tests;

public class VoiceCallTests
{
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static VoiceCall Ringing() => VoiceCall.Start("s1", "agent-1", "Elif", T0);

    [Fact]
    public void Start_IsRinging_AndOpen()
    {
        var call = Ringing();
        call.Status.Should().Be(VoiceCallStatus.Ringing);
        call.IsOpen.Should().BeTrue();
        call.ConsentAt.Should().BeNull();
    }

    [Fact]
    public void Accept_RecordsConsent_AndActivates()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Status.Should().Be(VoiceCallStatus.Active);
        call.ConsentAt.Should().Be(T0.AddSeconds(5));
        call.AnsweredAt.Should().Be(T0.AddSeconds(5));
    }

    [Fact]
    public void Accept_AfterEnd_Throws()
    {
        var call = Ringing();
        call.Decline(T0.AddSeconds(2), VoiceCallEndReasons.Declined);
        var act = () => call.Accept(T0.AddSeconds(3));
        act.Should().Throw<VoiceCallStateException>();
    }

    [Fact]
    public void Hangup_WhileRinging_ByAgent_IsCancelled()
    {
        var call = Ringing();
        call.Hangup(T0.AddSeconds(3), VoiceCallEndReasons.AgentHangup);
        call.Status.Should().Be(VoiceCallStatus.Cancelled);
        call.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Hangup_WhileActive_IsEnded_WithDuration()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Hangup(T0.AddSeconds(65), VoiceCallEndReasons.CustomerHangup);
        call.Status.Should().Be(VoiceCallStatus.Ended);
        call.Duration.Should().Be(TimeSpan.FromSeconds(60));
        call.EndReason.Should().Be(VoiceCallEndReasons.CustomerHangup);
    }

    [Theory]
    [InlineData(VoiceCallEndReasons.ConnectionLost)]
    [InlineData(VoiceCallEndReasons.ConnectFailed)]
    public void Hangup_WithConnectionProblem_IsFailed(string reason)
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Hangup(T0.AddSeconds(30), reason);
        call.Status.Should().Be(VoiceCallStatus.Failed);
    }

    [Fact]
    public void Miss_OnlyFromRinging()
    {
        var call = Ringing();
        call.Miss(T0.AddSeconds(45));
        call.Status.Should().Be(VoiceCallStatus.Missed);
        call.EndReason.Should().Be(VoiceCallEndReasons.Missed);

        var active = Ringing();
        active.Accept(T0.AddSeconds(1));
        var act = () => active.Miss(T0.AddSeconds(45));
        act.Should().Throw<VoiceCallStateException>();
    }

    [Fact]
    public void Hangup_Twice_IsNoOp()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(1));
        call.Hangup(T0.AddSeconds(10), VoiceCallEndReasons.AgentHangup);
        call.Hangup(T0.AddSeconds(20), VoiceCallEndReasons.CustomerHangup);
        call.EndedAt.Should().Be(T0.AddSeconds(10));
        call.EndReason.Should().Be(VoiceCallEndReasons.AgentHangup);
    }
}
