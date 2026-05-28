using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Services.UiHint;

/// <summary>
/// Session ID tabanlı UI hint deposu.
/// IApprovalContextAccessor üzerinden session ID okur — SDK içinden de güvenilir çalışır.
/// </summary>
public sealed class UiHintEmitter : IUiHintEmitter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<StreamEvent>> _store = new();
    private readonly IApprovalContextAccessor _ctx;

    public UiHintEmitter(IApprovalContextAccessor ctx) => _ctx = ctx;

    public void Emit(StreamEvent evt)
    {
        var sessionId = _ctx.Context?.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return;
        _store.GetOrAdd(sessionId, _ => new ConcurrentQueue<StreamEvent>()).Enqueue(evt);
    }

    public IReadOnlyList<StreamEvent> DrainPending(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)
            || !_store.TryGetValue(sessionId, out var queue)
            || queue.IsEmpty)
        {
            return [];
        }

        var result = new List<StreamEvent>();
        while (queue.TryDequeue(out var evt))
            result.Add(evt);

        if (queue.IsEmpty)
            _store.TryRemove(sessionId, out _);

        return result;
    }
}
