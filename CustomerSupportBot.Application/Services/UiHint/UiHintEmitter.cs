using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public bool Emit(StreamEvent evt)
    {
        var ctx = _ctx.Context;
        var sessionId = ctx?.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return false;
        _store.GetOrAdd(sessionId, _ => new ConcurrentQueue<StreamEvent>()).Enqueue(TagAgent(evt, ctx!.AgentName));
        return true;
    }

    /// <summary>
    /// Event'i ürettiği anda ambient bağlamdaki "şu an çalışan ajan" bilgisiyle etiketler.
    /// Böylece frontend, hint'in hangi ajana ait olduğunu drain zamanlamasına/sırasına
    /// (ör. o an ekranda görünen son "agent" event'ine) güvenerek tahmin etmek zorunda kalmaz.
    /// </summary>
    private static StreamEvent TagAgent(StreamEvent evt, string? agentName)
    {
        if (string.IsNullOrEmpty(agentName) || evt.Data is null) return evt;

        var node = JsonSerializer.SerializeToNode(evt.Data);
        if (node is not JsonObject obj) return evt;

        obj["agent"] = agentName;
        return evt with { Data = obj };
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
