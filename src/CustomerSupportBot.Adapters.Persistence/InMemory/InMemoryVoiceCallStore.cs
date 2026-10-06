// IVoiceCallStore'un bellek içi ikizi (API testleri). Postgres'teki partial unique index kuralını kilitle uygular.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryVoiceCallStore : IVoiceCallStore, ISessionDataEraser
{
    private readonly Dictionary<string, VoiceCall> _calls = new();
    private readonly Lock _gate = new();

    private static VoiceCall Copy(VoiceCall c) => new()
    {
        Id = c.Id, SessionId = c.SessionId, AgentId = c.AgentId, AgentDisplayName = c.AgentDisplayName,
        Status = c.Status, CreatedAt = c.CreatedAt, AnsweredAt = c.AnsweredAt, ConsentAt = c.ConsentAt,
        EndedAt = c.EndedAt, EndReason = c.EndReason, LastChunkAt = c.LastChunkAt
    };

    public Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_calls.Values.Any(c => c.IsOpen && (c.AgentId == call.AgentId || c.SessionId == call.SessionId)))
                return Task.FromResult(false);
            _calls[call.Id] = Copy(call);
            return Task.FromResult(true);
        }
    }

    public Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_calls.TryGetValue(id, out var c) ? Copy(c) : null);
    }

    public Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_calls.Values.Where(c => c.IsOpen && c.AgentId == agentId).Select(Copy).FirstOrDefault());
    }

    public Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<VoiceCall>>(_calls.Values.Where(c => c.IsOpen).Select(Copy).ToList());
    }

    public Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<VoiceCall>>(
                _calls.Values.Where(c => c.SessionId == sessionId).OrderBy(c => c.CreatedAt).Select(Copy).ToList());
    }

    public Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(call.Id, out var current) || current.Status != expectedStatus)
                return Task.FromResult(false);
            _calls[call.Id] = Copy(call);
            return Task.FromResult(true);
        }
    }

    public Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default)
    {
        lock (_gate) if (_calls.TryGetValue(callId, out var c)) c.LastChunkAt = at;
        return Task.CompletedTask;
    }

    public string Name => "voice_calls";

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var ids = _calls.Values.Where(c => sessionIds.Contains(c.SessionId)).Select(c => c.Id).ToList();
            ids.ForEach(id => _calls.Remove(id));
            return Task.FromResult(ids.Count);
        }
    }
}
