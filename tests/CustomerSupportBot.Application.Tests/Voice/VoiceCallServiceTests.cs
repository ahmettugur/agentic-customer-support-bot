// Sesli görüşme kuralları: yalnızca insan modunda başlar, temsilci başına tek görüşme, rıza = kabul,
// sinyal yalnızca karşı tarafa, görüşmenin tarafı olmayan reddedilir, zaman aşımları.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Voice;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CustomerSupportBot.Application.Tests.Voice;

public class VoiceCallServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StaffCaller Elif = new("agent-1", "Elif", false);
    private static readonly StaffCaller Can = new("agent-2", "Can", false);

    private readonly InMemoryVoiceCallStore _calls = new();
    private readonly InMemoryVoiceRecordingStore _recordings = new();
    private readonly IChatBridge _bridge = Substitute.For<IChatBridge>();
    private readonly IChatModeRegistry _modes = Substitute.For<IChatModeRegistry>();
    private readonly ISessionManager _sessions = Substitute.For<ISessionManager>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly VoiceCallOptions _options = new() { Turn = { Urls = ["turn:localhost:3478"], SharedSecret = "s" } };

    private VoiceCallService Service(ILogger<VoiceCallService>? logger = null) => new(_calls, _recordings, _bridge, _modes, _sessions,
        new OptionsMonitorStub<VoiceCallOptions>(_options), _time, logger ?? NullLogger<VoiceCallService>.Instance);

    private void HumanMode(string sid, string customerId = "1001")
    {
        _modes.GetMode(sid).Returns(ChatMode.Human);
        var session = new AgentSession { SessionId = sid };
        session.State.AuthenticatedCustomerId = customerId;
        _sessions.GetAsync(sid, Arg.Any<CancellationToken>()).Returns(session);
    }

    [Fact]
    public async Task Start_RequiresHumanMode()
    {
        _modes.GetMode("s1").Returns(ChatMode.Bot);
        (await Service().StartAsync("s1", Elif, Ct)).Error.Should().Be(VoiceCallError.NotInHumanMode);
    }

    [Fact]
    public async Task Start_RingsCustomer_AndSecondCallByAgentIsBusy()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var first = await svc.StartAsync("s1", Elif, Ct);
        first.Ok.Should().BeTrue();
        _bridge.Received(1).PublishVoiceSignal("s1", true, Arg.Is<string>(j => j.Contains("\"ring\"") && j.Contains(first.Call!.Id)));

        (await svc.StartAsync("s2", Elif, Ct)).Error.Should().Be(VoiceCallError.Busy);
    }

    [Fact]
    public async Task Accept_ByOwner_RecordsConsent_AndNotifiesAgent()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        var accepted = await svc.AcceptAsync(call.Id, "1001", Ct);
        accepted.Call!.Status.Should().Be(VoiceCallStatus.Active);
        accepted.Call.ConsentAt.Should().NotBeNull();
        _bridge.Received(1).PublishVoiceSignal("s1", false, Arg.Is<string>(j => j.Contains("\"accepted\"")));
    }

    [Fact]
    public async Task Accept_ByOtherCustomer_IsForbidden_AndStateUnchanged()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.AcceptAsync(call.Id, "9999", Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        (await _calls.GetAsync(call.Id, Ct))!.Status.Should().Be(VoiceCallStatus.Ringing);
    }

    [Fact]
    public async Task Accept_AfterDecline_IsInvalidState()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.DeclineAsync(call.Id, "1001", VoiceCallEndReasons.Declined, Ct);
        (await svc.AcceptAsync(call.Id, "1001", Ct)).Error.Should().Be(VoiceCallError.InvalidState);
    }

    [Fact]
    public async Task Signal_FromStaff_GoesToCustomer_OnlyForOwnCall()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);
        _bridge.ClearReceivedCalls();

        var payload = $$$"""{"callId":"{{{call.Id}}}","type":"offer","data":{"sdp":"x"}}""";
        (await svc.SignalFromStaffAsync(call.Id, Elif, payload, Ct)).Ok.Should().BeTrue();
        _bridge.Received(1).PublishVoiceSignal("s1", true, payload);

        (await svc.SignalFromStaffAsync(call.Id, Can, payload, Ct)).Error.Should().Be(VoiceCallError.Forbidden);
    }

    [Fact]
    public async Task Signal_WithMismatchedCallId_IsInvalid()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.SignalFromCustomerAsync(call.Id, "1001", """{"callId":"other","type":"ice"}""", Ct))
            .Error.Should().Be(VoiceCallError.Invalid);
    }

    [Fact]
    public async Task Hangup_WritesDurationNote_AndAgentCanCallAgain()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);
        _time.Advance(TimeSpan.FromSeconds(252));
        (await svc.HangupByCustomerAsync(call.Id, "1001", VoiceCallEndReasons.CustomerHangup, Ct)).Call!.Status
            .Should().Be(VoiceCallStatus.Ended);
        await _bridge.Received(1).PublishSystemMessageAsync("s1", "Sesli görüşme · 4 dk 12 sn");
        (await svc.StartAsync("s2", Elif, Ct)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task UploadChunk_OnlyByCallAgent_IdempotentAndSizeLimited()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);

        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Customer, 0, 0, 10000, "audio/webm", [1, 2], Ct)).Ok.Should().BeTrue();
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Customer, 0, 0, 10000, "audio/webm", [1, 2], Ct)).Ok.Should().BeTrue();
        (await _recordings.ListMetaAsync(call.Id, Ct)).Should().ContainSingle();

        (await svc.UploadChunkAsync(call.Id, Can, VoiceTrack.Agent, 0, 0, 10000, "audio/webm", [1], Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Agent, 1, 10000, 10000, "audio/webm", new byte[_options.MaxChunkBytes + 1], Ct))
            .Error.Should().Be(VoiceCallError.TooLarge);
    }

    [Fact]
    public async Task UploadChunk_BeforeConsent_IsInvalidState()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Agent, 0, 0, 10000, "audio/webm", [1], Ct))
            .Error.Should().Be(VoiceCallError.InvalidState);
    }

    [Fact]
    public async Task Sweep_MissesRingingAfter45s_AndFailsSilentActiveAfter60s()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var ringing = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        var active = (await svc.StartAsync("s2", Can, Ct)).Call!;
        await svc.AcceptAsync(active.Id, "1001", Ct);

        _time.Advance(TimeSpan.FromSeconds(46));
        (await svc.SweepAsync(Ct)).Should().Be(1);
        (await _calls.GetAsync(ringing.Id, Ct))!.Status.Should().Be(VoiceCallStatus.Missed);

        _time.Advance(TimeSpan.FromSeconds(20));
        (await svc.SweepAsync(Ct)).Should().Be(1);
        var failed = (await _calls.GetAsync(active.Id, Ct))!;
        failed.Status.Should().Be(VoiceCallStatus.Failed);
        failed.EndReason.Should().Be(VoiceCallEndReasons.ConnectionLost);
    }

    [Fact]
    public async Task IceConfig_ForCustomer_RequiresOwnership_AndHasShortLivedTurn()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.GetIceConfigForCustomerAsync(call.Id, "9999", Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        var (config, _) = await svc.GetIceConfigForCustomerAsync(call.Id, "1001", Ct);
        config!.IceServers.Should().Contain(s => s.Urls.Contains("turn:localhost:3478") && s.Username!.EndsWith(":" + call.Id));
        config.IceServers.Should().NotContain(s => s.Credential == "s", "paylaşılan sır asla istemciye gitmez");
    }

    [Fact]
    public async Task AgentsInCall_ListsAgentsWithOpenCalls_Only()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var active = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(active.Id, "1001", Ct);
        var cancelled = (await svc.StartAsync("s2", Can, Ct)).Call!;
        await svc.HangupByStaffAsync(cancelled.Id, Can, VoiceCallEndReasons.AgentHangup, Ct);

        (await svc.GetAgentsInCallAsync(Ct)).Should().BeEquivalentTo(["agent-1"]);
    }

    private async Task<(VoiceCallService Svc, VoiceCall Call)> ActiveCallAsync(ILogger<VoiceCallService>? logger = null)
    {
        HumanMode("s1");
        var svc = Service(logger);
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);
        _time.Advance(TimeSpan.FromSeconds(30));
        return (svc, call);
    }

    [Fact]
    public async Task Hangup_AwaitsDurationNote()
    {
        var (svc, call) = await ActiveCallAsync();
        var note = new TaskCompletionSource();
        _bridge.PublishSystemMessageAsync("s1", Arg.Any<string>()).Returns(note.Task);

        var hangup = svc.HangupByCustomerAsync(call.Id, "1001", VoiceCallEndReasons.CustomerHangup, Ct);
        hangup.IsCompleted.Should().BeFalse();

        note.SetResult();
        (await hangup).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Hangup_NoteFailure_IsLogged_AndCallStillEnds()
    {
        var logger = Substitute.For<ILogger<VoiceCallService>>();
        var (svc, call) = await ActiveCallAsync(logger);
        _bridge.PublishSystemMessageAsync("s1", Arg.Any<string>()).Returns(Task.FromException(new InvalidOperationException("db down")));

        var r = await svc.HangupByCustomerAsync(call.Id, "1001", VoiceCallEndReasons.CustomerHangup, Ct);

        r.Call!.Status.Should().Be(VoiceCallStatus.Ended);
        logger.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == "Log"
            && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning
            && c.GetArguments()[3] is InvalidOperationException);
    }
}
