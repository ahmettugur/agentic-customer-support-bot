using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Services.UiHint;

/// <summary>
/// Buffers hints only for an explicitly opened streaming turn.
/// </summary>
public sealed class UiHintEmitter : IUiHintEmitter
{
    private readonly AsyncLocal<TurnBuffer?> _current = new();
    private readonly IApprovalContextAccessor _ctx;

    public UiHintEmitter(IApprovalContextAccessor ctx) => _ctx = ctx;

    public IUiHintTurn BeginTurn(string? sessionId)
    {
        var previous = _current.Value;
        var buffer = new TurnBuffer(sessionId);
        _current.Value = buffer;
        return new TurnScope(() =>
        {
            lock (buffer)
            {
                buffer.Closed = true;
                buffer.Events.Clear();
            }
            _current.Value = previous;
        }, () =>
        {
            var prior = _current.Value;
            _current.Value = buffer;
            return new Activation(() => _current.Value = prior);
        });
    }

    public bool Emit(StreamEvent evt)
    {
        var ctx = _ctx.Context;
        var buffer = _current.Value;
        if (buffer is null || string.IsNullOrEmpty(buffer.SessionId)
            || !string.Equals(ctx?.SessionId, buffer.SessionId, StringComparison.Ordinal)) return false;
        lock (buffer)
        {
            if (buffer.Closed) return false;
            buffer.Events.Enqueue(TagAgent(evt, ctx?.AgentName));
            return true;
        }
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
        var buffer = _current.Value;
        if (buffer is null || !string.Equals(buffer.SessionId, sessionId, StringComparison.Ordinal)) return [];
        lock (buffer)
        {
            var events = buffer.Events.ToArray();
            buffer.Events.Clear();
            return events;
        }
    }

    private sealed class TurnBuffer(string? sessionId)
    {
        public string? SessionId { get; } = sessionId;
        public Queue<StreamEvent> Events { get; } = new();
        public bool Closed { get; set; }
    }

    private sealed class TurnScope(Action close, Func<IDisposable> activate) : IUiHintTurn
    {
        private Action? _close = close;
        public IDisposable Activate() => activate();
        public void Dispose() => Interlocked.Exchange(ref _close, null)?.Invoke();
    }

    private sealed class Activation(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
