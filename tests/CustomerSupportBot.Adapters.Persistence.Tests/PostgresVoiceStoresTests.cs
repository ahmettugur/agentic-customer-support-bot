// Gerçek Postgres: tek açık görüşme kuralı partial unique index ile ve eşzamanlı isteklerde de geçerli;
// parça idempotentliği; sahiplenme tek pod'a düşer; saklama sesi siler, dökümü korur; oturum silmesi.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model.Voice;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresVoiceStoresTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private PostgresVoiceCallStore Calls => new(fixture.DbFactory);
    private PostgresVoiceRecordingStore Recordings => new(fixture.DbFactory);

    private async Task<string> InsertSessionAsync()
    {
        var sessionId = $"vc-{Guid.NewGuid():N}";
        await using var ctx = fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity { SessionId = sessionId, CreatedAt = DateTime.UtcNow, LastActivity = DateTime.UtcNow, StateJson = "{}" });
        await ctx.SaveChangesAsync(Ct);
        return sessionId;
    }

    [Fact]
    public async Task ConcurrentStarts_ForSameAgent_OnlyOneSucceeds()
    {
        var agent = $"agent-{Guid.NewGuid():N}";
        var sessions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => InsertSessionAsync()));
        var results = await Task.WhenAll(sessions.Select(s =>
            new PostgresVoiceCallStore(fixture.DbFactory).TryCreateAsync(VoiceCall.Start(s, agent, "Elif", DateTime.UtcNow), Ct)));
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task SecondOpenCall_OnSameSession_IsRejected_UntilFirstEnds()
    {
        var sid = await InsertSessionAsync();
        var first = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        (await Calls.TryCreateAsync(first, Ct)).Should().BeTrue();
        (await Calls.TryCreateAsync(VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Can", DateTime.UtcNow), Ct)).Should().BeFalse();

        first.Hangup(DateTime.UtcNow, VoiceCallEndReasons.AgentHangup);
        (await Calls.TryUpdateAsync(first, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await Calls.TryCreateAsync(VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Can", DateTime.UtcNow), Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task TryUpdate_WithStaleStatus_ReturnsFalse()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        call.Accept(DateTime.UtcNow);
        (await Calls.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await Calls.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeFalse();
        (await Calls.GetAsync(call.Id, Ct))!.ConsentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Chunks_AreIdempotent_ClaimedOnce_AndPurgeKeepsTranscript()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        var created = DateTime.UtcNow.AddDays(-100);
        var chunk = new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Customer, Sequence = 0, OffsetMs = 0, DurationMs = 10000, Data = [1, 2, 3], CreatedAt = created, NextAttemptAt = created };
        (await Recordings.TryAddAsync(chunk, Ct)).Should().BeTrue();
        (await Recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Customer, Sequence = 0, Data = [9], CreatedAt = created, NextAttemptAt = created }, Ct)).Should().BeFalse();

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            new PostgresVoiceRecordingStore(fixture.DbFactory).TryClaimNextPendingAsync(DateTime.UtcNow, Ct)));
        claims.Count(c => c?.Id == chunk.Id).Should().Be(1);

        await Recordings.CompleteAsync(chunk.Id, "Kargom gelmedi.", Ct);
        (await Recordings.PurgeAudioCreatedBeforeAsync(DateTime.UtcNow.AddDays(-90), Ct)).Should().BeGreaterThanOrEqualTo(1);
        var after = await Recordings.GetAsync(chunk.Id, Ct);
        after!.Data.Should().BeEmpty();
        after.TranscriptText.Should().Be("Kargom gelmedi.");
    }

    [Fact]
    public async Task EraseSessions_RemovesCallsAndChunks()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        await Recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Agent, Sequence = 0, Data = [1], CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow }, Ct);

        (await Recordings.EraseSessionsAsync([sid], Ct)).Should().Be(1);
        (await Calls.EraseSessionsAsync([sid], Ct)).Should().Be(1);
        (await Calls.GetAsync(call.Id, Ct)).Should().BeNull();
    }
}
