// IVoiceRecordingStore'un bellek içi ikizi (API testleri).

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryVoiceRecordingStore : IVoiceRecordingStore, ISessionDataEraser
{
    private static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(5);
    private readonly List<VoiceRecordingChunk> _chunks = [];
    private readonly Dictionary<string, DateTime> _claimedAt = new();
    private readonly Lock _gate = new();

    private static VoiceRecordingChunk Copy(VoiceRecordingChunk c, bool withData) => new()
    {
        Id = c.Id, CallId = c.CallId, SessionId = c.SessionId, Track = c.Track, Sequence = c.Sequence,
        OffsetMs = c.OffsetMs, DurationMs = c.DurationMs, ContentType = c.ContentType,
        Data = withData ? c.Data : [], TranscriptStatus = c.TranscriptStatus, TranscriptText = c.TranscriptText,
        Attempts = c.Attempts, NextAttemptAt = c.NextAttemptAt, AudioPurgedAt = c.AudioPurgedAt, CreatedAt = c.CreatedAt
    };

    public Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_chunks.Any(c => c.CallId == chunk.CallId && c.Track == chunk.Track && c.Sequence == chunk.Sequence))
                return Task.FromResult(false);
            _chunks.Add(Copy(chunk, withData: true));
            return Task.FromResult(true);
        }
    }

    public Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var next = _chunks
                .Where(c => (c.TranscriptStatus == VoiceTranscriptStatus.Pending && c.NextAttemptAt <= now)
                         || (c.TranscriptStatus == VoiceTranscriptStatus.Processing
                             && _claimedAt.TryGetValue(c.Id, out var at) && now - at > StaleProcessing))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Sequence)
                .FirstOrDefault();
            if (next is null) return Task.FromResult<VoiceRecordingChunk?>(null);
            next.TranscriptStatus = VoiceTranscriptStatus.Processing;
            _claimedAt[next.Id] = now;
            return Task.FromResult<VoiceRecordingChunk?>(Copy(next, withData: true));
        }
    }

    public Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.TranscriptStatus = VoiceTranscriptStatus.Done;
            c.TranscriptText = text;
        }
        return Task.CompletedTask;
    }

    public Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.Attempts++;
            c.NextAttemptAt = nextAttemptAt;
            c.TranscriptStatus = final ? VoiceTranscriptStatus.Failed : VoiceTranscriptStatus.Pending;
        }
        return Task.CompletedTask;
    }

    public Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.NextAttemptAt = nextAttemptAt;
            c.TranscriptStatus = VoiceTranscriptStatus.Pending;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<VoiceRecordingChunk>>(_chunks
                .Where(c => c.CallId == callId).OrderBy(c => c.OffsetMs).ThenBy(c => c.Track)
                .Select(c => Copy(c, withData: false)).ToList());
    }

    public Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_chunks.FirstOrDefault(c => c.Id == chunkId) is { } c ? Copy(c, withData: true) : null);
    }

    public Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _chunks.Where(c => c.CreatedAt < cutoffUtc && c.AudioPurgedAt is null).ToList();
            foreach (var c in old) { c.Data = []; c.AudioPurgedAt = DateTime.UtcNow; }
            return Task.FromResult(old.Count);
        }
    }

    public string Name => "voice_recordings";

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_chunks.RemoveAll(c => sessionIds.Contains(c.SessionId)));
    }
}
