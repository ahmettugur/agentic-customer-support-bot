// Bellek içi ikizler Postgres sözleşmesiyle aynı kuralları uygulamalı (API testleri bunları kullanır).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryVoiceStoresTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CallStore_AllowsOneOpenCallPerAgent_AndPerSession()
    {
        var store = new InMemoryVoiceCallStore();
        (await store.TryCreateAsync(VoiceCall.Start("s1", "a1", "Elif", T0), Ct)).Should().BeTrue();
        (await store.TryCreateAsync(VoiceCall.Start("s2", "a1", "Elif", T0), Ct)).Should().BeFalse("temsilci zaten görüşmede");
        (await store.TryCreateAsync(VoiceCall.Start("s1", "a2", "Can", T0), Ct)).Should().BeFalse("oturumda açık görüşme var");
    }

    [Fact]
    public async Task CallStore_AfterEnd_AgentCanCallAgain()
    {
        var store = new InMemoryVoiceCallStore();
        var call = VoiceCall.Start("s1", "a1", "Elif", T0);
        await store.TryCreateAsync(call, Ct);
        call.Hangup(T0.AddSeconds(1), VoiceCallEndReasons.AgentHangup);
        (await store.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await store.TryCreateAsync(VoiceCall.Start("s2", "a1", "Elif", T0.AddSeconds(2)), Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CallStore_TryUpdate_RejectsStaleExpectedStatus()
    {
        var store = new InMemoryVoiceCallStore();
        var call = VoiceCall.Start("s1", "a1", "Elif", T0);
        await store.TryCreateAsync(call, Ct);
        var copy = (await store.GetAsync(call.Id, Ct))!;
        call.Accept(T0.AddSeconds(1));
        (await store.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        copy.Hangup(T0.AddSeconds(2), VoiceCallEndReasons.AgentHangup);
        (await store.TryUpdateAsync(copy, VoiceCallStatus.Ringing, Ct)).Should().BeFalse("kalıcı durum artık Active");
    }

    [Fact]
    public async Task RecordingStore_IgnoresDuplicateChunk_AndClaimsOnce()
    {
        var store = new InMemoryVoiceRecordingStore();
        var chunk = new VoiceRecordingChunk { CallId = "c1", SessionId = "s1", Track = VoiceTrack.Customer, Sequence = 0, Data = [1, 2], CreatedAt = T0, NextAttemptAt = T0 };
        (await store.TryAddAsync(chunk, Ct)).Should().BeTrue();
        (await store.TryAddAsync(new VoiceRecordingChunk { CallId = "c1", Track = VoiceTrack.Customer, Sequence = 0, Data = [9], CreatedAt = T0, NextAttemptAt = T0 }, Ct)).Should().BeFalse();

        var claimed = await store.TryClaimNextPendingAsync(T0, Ct);
        claimed!.Data.Should().Equal(1, 2);
        (await store.TryClaimNextPendingAsync(T0, Ct)).Should().BeNull("zaten Processing");
    }

    [Fact]
    public async Task RecordingStore_Purge_KeepsTranscript()
    {
        var store = new InMemoryVoiceRecordingStore();
        var chunk = new VoiceRecordingChunk { CallId = "c1", SessionId = "s1", Track = VoiceTrack.Agent, Sequence = 0, Data = [1], CreatedAt = T0, NextAttemptAt = T0 };
        await store.TryAddAsync(chunk, Ct);
        await store.CompleteAsync(chunk.Id, "Merhaba", Ct);

        (await store.PurgeAudioCreatedBeforeAsync(T0.AddDays(1), Ct)).Should().Be(1);
        var after = await store.GetAsync(chunk.Id, Ct);
        after!.Data.Should().BeEmpty();
        after.TranscriptText.Should().Be("Merhaba");
        after.AudioPurgedAt.Should().NotBeNull();
    }
}
