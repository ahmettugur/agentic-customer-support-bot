// Döküm: konuşan etiketiyle yalnız temsilciye giden satır, geçmişe ekleme, boş parça satırsız,
// 3 başarısız denemeden sonra "(döküm alınamadı)", bütçe doluyken deneme harcanmadan ertelenir.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Voice;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CustomerSupportBot.Application.Tests.Voice;

public class VoiceTranscriptionProcessorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly InMemoryVoiceCallStore _calls = new();
    private readonly InMemoryVoiceRecordingStore _recordings = new();
    private readonly IChatBridge _bridge = Substitute.For<IChatBridge>();
    private readonly ISessionManager _sessions = Substitute.For<ISessionManager>();
    private readonly IAudioTranscriber _transcriber = Substitute.For<IAudioTranscriber>();
    private readonly ILlmSpendGuard _budget = Substitute.For<ILlmSpendGuard>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    private VoiceTranscriptionProcessor Processor() => new(_recordings, _calls, _bridge, _sessions, _transcriber, _budget,
        new OptionsMonitorStub<VoiceCallOptions>(new VoiceCallOptions()), _time, NullLogger<VoiceTranscriptionProcessor>.Instance);

    private async Task<(VoiceCall Call, VoiceRecordingChunk Chunk)> SeedAsync(VoiceTrack track)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var call = VoiceCall.Start("s1", "agent-1", "Elif", now);
        call.Accept(now);
        await _calls.TryCreateAsync(call, Ct);
        var chunk = new VoiceRecordingChunk { CallId = call.Id, SessionId = "s1", Track = track, Sequence = 2, OffsetMs = 20000, DurationMs = 10000, Data = [1], CreatedAt = now, NextAttemptAt = now };
        await _recordings.TryAddAsync(chunk, Ct);
        return (call, chunk);
    }

    [Fact]
    public async Task CustomerChunk_PublishesLabelledLine_AndAppendsUserMessage()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(" Kargom gelmedi. ");

        (await Processor().ProcessNextAsync(Ct)).Should().BeTrue();

        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "customer", 20000, "Müşteri: Kargom gelmedi.");
        await _sessions.Received(1).AppendUserMessageAsync("s1", "Kargom gelmedi.", Arg.Any<CancellationToken>());
        (await _recordings.GetAsync(chunk.Id, Ct))!.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Done);
    }

    [Fact]
    public async Task AgentChunk_UsesAgentName_AndAppendsAssistantMessage()
    {
        var (call, _) = await SeedAsync(VoiceTrack.Agent);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Hemen bakıyorum.");

        await Processor().ProcessNextAsync(Ct);

        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "agent", 20000, "Temsilci (Elif): Hemen bakıyorum.");
        await _sessions.Received(1).AppendAssistantMessageAsync("s1", "Hemen bakıyorum.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SilentChunk_ProducesNoLine()
    {
        await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("   ");
        await Processor().ProcessNextAsync(Ct);
        await _bridge.DidNotReceiveWithAnyArgs().PublishVoiceTranscriptAsync(default!, default!, default!, default, default!);
    }

    [Fact]
    public async Task ThreeFailures_MarkFailed_AndPublishPlaceholder()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("503"));
        var p = Processor();

        for (var i = 0; i < 3; i++)
        {
            (await p.ProcessNextAsync(Ct)).Should().BeTrue();
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        (await _recordings.GetAsync(chunk.Id, Ct))!.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Failed);
        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "customer", 20000, "Müşteri: (döküm alınamadı)");
    }

    [Fact]
    public async Task BudgetExceeded_PostponesWithoutSpendingAttempt()
    {
        var (_, chunk) = await SeedAsync(VoiceTrack.Customer);
        _budget.CheckAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new LlmBudgetExceeded(LlmBudgetScope.Daily, 10m, 10m));

        await Processor().ProcessNextAsync(Ct);

        var after = (await _recordings.GetAsync(chunk.Id, Ct))!;
        after.Attempts.Should().Be(0);
        after.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Pending);
        await _transcriber.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, default);
    }

    [Fact]
    public async Task DuplicateUpload_YieldsSingleLine()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        await _recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = "s1", Track = VoiceTrack.Customer, Sequence = 2, Data = [1], CreatedAt = chunk.CreatedAt, NextAttemptAt = chunk.CreatedAt }, Ct);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Merhaba");
        var p = Processor();
        await p.ProcessNextAsync(Ct);
        (await p.ProcessNextAsync(Ct)).Should().BeFalse();
        await _bridge.Received(1).PublishVoiceTranscriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }
}
