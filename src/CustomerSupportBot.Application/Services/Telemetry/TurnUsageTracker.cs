// Application/Services/Telemetry/TurnUsageTracker.cs

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Application.Services.Telemetry;

/// <summary>Oturum kimliğiyle anahtarlanan tur kovaları (singleton). Aynı oturumda tur kilidi tek turu garanti eder.</summary>
public sealed class TurnUsageTracker(ILlmCallAttribution attribution) : ITurnUsageTracker
{
    private readonly ConcurrentDictionary<string, Bucket> _turns = new(StringComparer.Ordinal);

    public void Begin(string sessionId) => _turns[sessionId] = new Bucket();

    public void Record(string label, long inputTokens, long outputTokens)
    {
        var sessionId = attribution.CurrentSessionId;
        if (sessionId is null || !_turns.TryGetValue(sessionId, out var bucket)) return;
        bucket.Add(label, inputTokens, outputTokens);
    }

    public TurnUsageSummary? Complete(string sessionId) =>
        _turns.TryRemove(sessionId, out var bucket) ? bucket.ToSummary() : null;

    private sealed class Bucket
    {
        private readonly object _gate = new();
        private readonly List<string> _order = [];
        private readonly Dictionary<string, (long In, long Out, int Calls)> _lines = new(StringComparer.Ordinal);

        public void Add(string label, long input, long output)
        {
            lock (_gate)
            {
                if (_lines.TryGetValue(label, out var cur))
                    _lines[label] = (cur.In + input, cur.Out + output, cur.Calls + 1);
                else
                {
                    _order.Add(label);
                    _lines[label] = (input, output, 1);
                }
            }
        }

        public TurnUsageSummary? ToSummary()
        {
            lock (_gate)
            {
                if (_order.Count == 0) return null;
                return new TurnUsageSummary(_order.Select(l => new TurnUsageLine(l, _lines[l].In, _lines[l].Out, _lines[l].Calls)).ToList());
            }
        }
    }
}
