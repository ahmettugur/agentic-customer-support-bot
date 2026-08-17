// Services/Persistence/PostgresSlaEventSink.cs
// Hibrit SLA Event sink — in-memory ring buffer + PostgreSQL write-through.
// Son 500 olay bellekte tutulur; LastEmittedAt aynı target+severity için
// tekrar event üretilmesini engeller. Singleton servis.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Record() sonrası TAM event yayınlanır — uzak pod'lar kendi ring buffer'ına
//     ekler ve EventRecorded'ı tetikler (SLA dashboard'un canlı akışı için önemli).

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresSlaEventSink : ISlaEventSink
{
    private const string ChannelRecorded = "csbot:sla:recorded";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresSlaEventSink> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentQueue<SlaEvent> _events = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastEmittedAt = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;
    private const int Capacity = 500;

    public event EventHandler<SlaEvent>? EventRecorded;

    public PostgresSlaEventSink(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresSlaEventSink> logger)
    {
        _dbFactory = dbFactory;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelRecorded, OnRemoteRecorded);
    }

    public void Record(SlaEvent evt)
    {
        _events.Enqueue(evt);
        while (_events.Count > Capacity && _events.TryDequeue(out _)) { }

        var key = Key(evt.Kind, evt.TargetId, evt.Severity);
        _lastEmittedAt[key] = evt.Timestamp;

        _logger.LogInformation(
            "[SLA] {Severity} {Kind}={TargetId} age={Age}s action={Action}",
            evt.Severity, evt.Kind, evt.TargetId, evt.AgeSeconds, evt.Action ?? "-");

        try
        {
            InsertAsync(evt).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SLA] DB INSERT başarısız. Id={Id}", evt.Id);
        }

        try { EventRecorded?.Invoke(this, evt); }
        catch (Exception ex) { _logger.LogWarning(ex, "EventRecorded handler failed"); }

        PublishRecorded(evt);
    }

    public IReadOnlyList<SlaEvent> GetRecent(int count = 100)
    {
        EnsureHydrated();
        return _events.Reverse().Take(count).ToList();
    }

    public DateTime? LastEmittedAt(string kind, string targetId, string severity)
    {
        EnsureHydrated();
        return _lastEmittedAt.TryGetValue(Key(kind, targetId, severity), out var ts) ? ts : null;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task InsertAsync(SlaEvent evt)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.SlaEvents.Add(new SlaEventEntity
        {
            Id = evt.Id,
            Timestamp = evt.Timestamp,
            Kind = evt.Kind,
            Severity = evt.Severity,
            TargetId = evt.TargetId,
            AgeSeconds = evt.AgeSeconds,
            Action = evt.Action,
            Note = evt.Note
        });
        await ctx.SaveChangesAsync();
    }

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SLA] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.SlaEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .Take(Capacity)
            .ToListAsync();

        foreach (var e in rows.AsEnumerable().Reverse())
        {
            _events.Enqueue(new SlaEvent
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                Kind = e.Kind,
                Severity = e.Severity,
                TargetId = e.TargetId,
                AgeSeconds = e.AgeSeconds,
                Action = e.Action,
                Note = e.Note
            });

            var key = Key(e.Kind, e.TargetId, e.Severity);
            _lastEmittedAt.TryAdd(key, e.Timestamp);
        }

        _logger.LogInformation("[SLA] Cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private static string Key(string kind, string targetId, string severity)
        => $"{kind}|{targetId}|{severity}";

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishRecorded(SlaEvent evt)
    {
        var payload = new { nodeId = _messageBus.NodeId, evt };
        _messageBus.Publish(ChannelRecorded, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteRecorded(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var evt = JsonSerializer.Deserialize<SlaEvent>(root.GetProperty("evt").GetRawText());
            if (evt is null) return;

            _events.Enqueue(evt);
            while (_events.Count > Capacity && _events.TryDequeue(out _)) { }
            _lastEmittedAt[Key(evt.Kind, evt.TargetId, evt.Severity)] = evt.Timestamp;

            try { EventRecorded?.Invoke(this, evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "EventRecorded handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SLA] Redis OnRemoteRecorded parse hatası");
        }
    }
}

