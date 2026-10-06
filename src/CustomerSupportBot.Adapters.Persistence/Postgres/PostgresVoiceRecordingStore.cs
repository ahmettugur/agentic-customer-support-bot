// Kayıt parçaları — doğrudan PostgreSQL. Listeleme sesi OKUMAZ. Döküm kuyruğu sahiplenmesi koşullu
// UPDATE ile: aynı parçayı iki pod birlikte işleyemez.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresVoiceRecordingStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IVoiceRecordingStore, ISessionDataEraser
{
    private static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(5);
    private static readonly string Pending = nameof(VoiceTranscriptStatus.Pending);
    private static readonly string Processing = nameof(VoiceTranscriptStatus.Processing);

    private static VoiceRecordingChunk ToDomain(VoiceRecordingChunkEntity e, bool withData) => new()
    {
        Id = e.Id, CallId = e.CallId, SessionId = e.SessionId, Track = Enum.Parse<VoiceTrack>(e.Track),
        Sequence = e.Sequence, OffsetMs = e.OffsetMs, DurationMs = e.DurationMs, ContentType = e.ContentType,
        Data = withData ? e.Data : [], TranscriptStatus = Enum.Parse<VoiceTranscriptStatus>(e.TranscriptStatus),
        TranscriptText = e.TranscriptText, Attempts = e.Attempts, NextAttemptAt = e.NextAttemptAt,
        AudioPurgedAt = e.AudioPurgedAt, CreatedAt = e.CreatedAt
    };

    public async Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.VoiceRecordingChunks.Add(new VoiceRecordingChunkEntity
        {
            Id = chunk.Id, CallId = chunk.CallId, SessionId = chunk.SessionId, Track = chunk.Track.ToString(),
            Sequence = chunk.Sequence, OffsetMs = chunk.OffsetMs, DurationMs = chunk.DurationMs,
            ContentType = chunk.ContentType, SizeBytes = chunk.Data.Length, Data = chunk.Data,
            TranscriptStatus = chunk.TranscriptStatus.ToString(), TranscriptText = chunk.TranscriptText,
            Attempts = chunk.Attempts, NextAttemptAt = chunk.NextAttemptAt, CreatedAt = chunk.CreatedAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public async Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var staleBefore = now - StaleProcessing;
        // Birkaç aday: biri başka pod'a kaptırılırsa sıradakini dene.
        var candidates = await db.VoiceRecordingChunks.AsNoTracking()
            .Where(c => (c.TranscriptStatus == Pending && c.NextAttemptAt <= now)
                     || (c.TranscriptStatus == Processing && c.ClaimedAt < staleBefore))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Sequence)
            .Select(c => new { c.Id, c.TranscriptStatus, c.ClaimedAt })
            .Take(5)
            .ToListAsync(ct);

        foreach (var cand in candidates)
        {
            var claimed = await db.VoiceRecordingChunks
                .Where(c => c.Id == cand.Id && c.TranscriptStatus == cand.TranscriptStatus && c.ClaimedAt == cand.ClaimedAt)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.TranscriptStatus, Processing)
                    .SetProperty(c => c.ClaimedAt, now), ct) > 0;
            if (!claimed) continue;
            var e = await db.VoiceRecordingChunks.AsNoTracking().FirstAsync(c => c.Id == cand.Id, ct);
            return ToDomain(e, withData: true);
        }
        return null;
    }

    public async Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.TranscriptStatus, nameof(VoiceTranscriptStatus.Done))
            .SetProperty(c => c.TranscriptText, text), ct);
    }

    public async Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var status = final ? nameof(VoiceTranscriptStatus.Failed) : Pending;
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Attempts, c => c.Attempts + 1)
            .SetProperty(c => c.NextAttemptAt, nextAttemptAt)
            .SetProperty(c => c.TranscriptStatus, status), ct);
    }

    public async Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.NextAttemptAt, nextAttemptAt)
            .SetProperty(c => c.TranscriptStatus, Pending), ct);
    }

    public async Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceRecordingChunks.AsNoTracking()
            .Where(c => c.CallId == callId)
            .OrderBy(c => c.OffsetMs).ThenBy(c => c.Track)
            .Select(c => new VoiceRecordingChunkEntity
            {
                Id = c.Id, CallId = c.CallId, SessionId = c.SessionId, Track = c.Track, Sequence = c.Sequence,
                OffsetMs = c.OffsetMs, DurationMs = c.DurationMs, ContentType = c.ContentType,
                TranscriptStatus = c.TranscriptStatus, TranscriptText = c.TranscriptText, Attempts = c.Attempts,
                NextAttemptAt = c.NextAttemptAt, AudioPurgedAt = c.AudioPurgedAt, CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);
        return list.Select(e => ToDomain(e, withData: false)).ToList();
    }

    public async Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceRecordingChunks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chunkId, ct);
        return e is null ? null : ToDomain(e, withData: true);
    }

    public async Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        return await db.VoiceRecordingChunks
            .Where(c => c.CreatedAt < cutoffUtc && c.AudioPurgedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Data, Array.Empty<byte>())
                .SetProperty(c => c.SizeBytes, 0)
                .SetProperty(c => c.AudioPurgedAt, now), ct);
    }

    public string Name => "voice_recordings";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.VoiceRecordingChunks.Where(c => sessionIds.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
    }
}
