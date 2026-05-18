// Services/Sla/InMemorySlaEventSink.cs
// SLA olay kayıtları için thread-safe in-memory store. Son 500 olay tutulur,
// LastEmittedAt sayesinde aynı target+severity tekrar tekrar emit edilmez.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public class InMemorySlaEventSink : ISlaEventSink
{
    private readonly ConcurrentQueue<SlaEvent> _events = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastEmittedAt = new();
    private readonly ILogger<InMemorySlaEventSink> _logger;
    private const int Capacity = 500;

    public event EventHandler<SlaEvent>? EventRecorded;

    public InMemorySlaEventSink(ILogger<InMemorySlaEventSink> logger)
    {
        _logger = logger;
    }

    public void Record(SlaEvent evt)
    {
        _events.Enqueue(evt);
        while (_events.Count > Capacity && _events.TryDequeue(out _)) { /* trim */ }

        var key = Key(evt.Kind, evt.TargetId, evt.Severity);
        _lastEmittedAt[key] = evt.Timestamp;

        _logger.LogInformation(
            "[SLA] {Severity} {Kind}={TargetId} age={Age}s action={Action}",
            evt.Severity, evt.Kind, evt.TargetId, evt.AgeSeconds, evt.Action ?? "-");

        try { EventRecorded?.Invoke(this, evt); }
        catch (Exception ex) { _logger.LogWarning(ex, "EventRecorded handler failed"); }
    }

    public IReadOnlyList<SlaEvent> GetRecent(int count = 100)
    {
        return _events.Reverse().Take(count).ToList();
    }

    public DateTime? LastEmittedAt(string kind, string targetId, string severity)
    {
        return _lastEmittedAt.TryGetValue(Key(kind, targetId, severity), out var ts)
            ? ts
            : null;
    }

    private static string Key(string kind, string targetId, string severity)
        => $"{kind}|{targetId}|{severity}";
}

