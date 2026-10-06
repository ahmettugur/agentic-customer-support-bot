// KVKK: sesli görüşme kayıtlarının sesi 90 gün sonra silinir (döküm kalır); dışa aktarım dökümü içerir;
// müşteri verisi silinince görüşmeler ve kayıtları da silinir.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Privacy;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Voice;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class DataPrivacyVoiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(DataPrivacyService Service, InMemorySessionManager Sessions,
        InMemoryVoiceCallStore Calls, InMemoryVoiceRecordingStore Recordings);

    private static Fixture Build(DataRetentionOptions? options = null)
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var attachments = new InMemoryAttachmentStore();
        var calls = new InMemoryVoiceCallStore();
        var recordings = new InMemoryVoiceRecordingStore();
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetHistoryForCustomerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ApprovalRequest>());
        var orders = Substitute.For<IOrderRepository>();
        orders.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, OrderInfo)>());
        var complaints = Substitute.For<IComplaintRepository>();
        complaints.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, ComplaintInfo)>());

        var service = new DataPrivacyService(
            sessions, attachments, [attachments, calls, recordings], [],
            new InMemoryCustomerProfileStore(), Substitute.For<IRatingStore>(), approvals, orders, complaints,
            Options.Create(options ?? new DataRetentionOptions()), NullLogger<DataPrivacyService>.Instance,
            dispositions: null, voiceRecordings: recordings, voiceCalls: calls);
        return new(service, sessions, calls, recordings);
    }

    private static async Task<(string Sid, VoiceCall Call)> CallWithTranscriptAsync(Fixture f, string customerId, DateTime createdAt)
    {
        var s = await f.Sessions.GetOrCreateAsync(null, Ct);
        s.State.AuthenticatedCustomerId = customerId;
        await f.Sessions.AddExchangeAsync(s.SessionId, "merhaba", "buyrun", ct: Ct);
        var call = VoiceCall.Start(s.SessionId, "agent-1", "Elif", createdAt);
        call.Accept(createdAt);
        call.Hangup(createdAt.AddSeconds(30), VoiceCallEndReasons.AgentHangup);
        await f.Calls.TryCreateAsync(call, Ct);
        foreach (var (track, offset, text) in new[] { (VoiceTrack.Customer, 0, "Kargom gelmedi"), (VoiceTrack.Agent, 10000, "Hemen bakıyorum") })
        {
            var chunk = new VoiceRecordingChunk { CallId = call.Id, SessionId = s.SessionId, Track = track, Sequence = offset / 10000, OffsetMs = offset, DurationMs = 10000, Data = [1, 2], CreatedAt = createdAt, NextAttemptAt = createdAt };
            await f.Recordings.TryAddAsync(chunk, Ct);
            await f.Recordings.CompleteAsync(chunk.Id, text, Ct);
        }
        return (s.SessionId, call);
    }

    [Fact]
    public async Task Retention_PurgesAudioOlderThanVoiceRetention_KeepsTranscript()
    {
        var f = Build(new DataRetentionOptions { VoiceRecordingRetentionDays = 90, ConversationRetentionDays = 0, AttachmentRetentionDays = 0 });
        var now = DateTime.UtcNow;
        var (_, oldCall) = await CallWithTranscriptAsync(f, "1001", now.AddDays(-100));
        var (_, newCall) = await CallWithTranscriptAsync(f, "1001", now.AddDays(-10));

        var result = await f.Service.RunRetentionAsync(now, Ct);

        result.VoiceRecordingsPurged.Should().Be(2);
        var oldChunk = (await f.Recordings.ListMetaAsync(oldCall.Id, Ct)).First();
        (await f.Recordings.GetAsync(oldChunk.Id, Ct))!.Data.Should().BeEmpty();
        oldChunk.TranscriptText.Should().NotBeNull();
        var newChunk = (await f.Recordings.ListMetaAsync(newCall.Id, Ct)).First();
        (await f.Recordings.GetAsync(newChunk.Id, Ct))!.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_IncludesVoiceTranscriptLines_InOrder()
    {
        var f = Build();
        await CallWithTranscriptAsync(f, "1001", DateTime.UtcNow);

        var export = await f.Service.ExportCustomerDataAsync("1001", Ct);

        var call = export.Sessions.Single().VoiceCalls!.Single();
        call.AgentDisplayName.Should().Be("Elif");
        call.DurationSeconds.Should().Be(30);
        call.Lines.Select(l => (l.Speaker, l.Text)).Should().Equal(("Müşteri", "Kargom gelmedi"), ("Temsilci", "Hemen bakıyorum"));
    }

    [Fact]
    public async Task Erase_RemovesCallsAndRecordings()
    {
        var f = Build();
        var (sid, call) = await CallWithTranscriptAsync(f, "1001", DateTime.UtcNow);

        await f.Service.EraseCustomerDataAsync("1001", Ct);

        (await f.Calls.ListForSessionAsync(sid, Ct)).Should().BeEmpty();
        (await f.Recordings.ListMetaAsync(call.Id, Ct)).Should().BeEmpty();
    }
}
