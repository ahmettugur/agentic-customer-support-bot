// Sesli görüşmeler — doğrudan PostgreSQL. Tek açık görüşme kuralı partial unique index'te; ekleme
// çakışması (23505) "meşgul" demektir. Durum değişiklikleri koşullu UPDATE ile (yarışan kapatmalar).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresVoiceCallStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IVoiceCallStore, ISessionDataEraser
{
    private static readonly string[] OpenStatuses = [nameof(VoiceCallStatus.Ringing), nameof(VoiceCallStatus.Active)];

    private static VoiceCall ToDomain(VoiceCallEntity e) => new()
    {
        Id = e.Id, SessionId = e.SessionId, AgentId = e.AgentId, AgentDisplayName = e.AgentDisplayName,
        Status = Enum.Parse<VoiceCallStatus>(e.Status), CreatedAt = e.CreatedAt, AnsweredAt = e.AnsweredAt,
        ConsentAt = e.ConsentAt, EndedAt = e.EndedAt, EndReason = e.EndReason, LastChunkAt = e.LastChunkAt
    };

    public async Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.VoiceCalls.Add(new VoiceCallEntity
        {
            Id = call.Id, SessionId = call.SessionId, AgentId = call.AgentId, AgentDisplayName = call.AgentDisplayName,
            Status = call.Status.ToString(), CreatedAt = call.CreatedAt, AnsweredAt = call.AnsweredAt,
            ConsentAt = call.ConsentAt, EndedAt = call.EndedAt, EndReason = call.EndReason, LastChunkAt = call.LastChunkAt
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

    public async Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceCalls.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return e is null ? null : ToDomain(e);
    }

    public async Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceCalls.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgentId == agentId && OpenStatuses.Contains(c.Status), ct);
        return e is null ? null : ToDomain(e);
    }

    public async Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceCalls.AsNoTracking().Where(c => OpenStatuses.Contains(c.Status)).ToListAsync(ct);
        return list.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceCalls.AsNoTracking().Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return list.Select(ToDomain).ToList();
    }

    public async Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var expected = expectedStatus.ToString();
        var status = call.Status.ToString();
        return await db.VoiceCalls
            .Where(c => c.Id == call.Id && c.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.AnsweredAt, call.AnsweredAt)
                .SetProperty(c => c.ConsentAt, call.ConsentAt)
                .SetProperty(c => c.EndedAt, call.EndedAt)
                .SetProperty(c => c.EndReason, call.EndReason), ct) > 0;
    }

    public async Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceCalls.Where(c => c.Id == callId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastChunkAt, at), ct);
    }

    public string Name => "voice_calls";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.VoiceCalls.Where(c => sessionIds.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
    }
}
