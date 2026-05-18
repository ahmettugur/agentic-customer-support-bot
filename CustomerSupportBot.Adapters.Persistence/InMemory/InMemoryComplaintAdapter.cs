// Adapters.Persistence/InMemory/InMemoryComplaintAdapter.cs
// DRIVEN ADAPTER — IComplaintRepository → InMemory (FakeDatabase.ComplaintsDb sarar).

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

/// <summary>
/// FakeDatabase şikayet deposunu IComplaintRepository port'una bağlar.
/// </summary>
public sealed class InMemoryComplaintAdapter : IComplaintRepository
{
    private readonly ConcurrentDictionary<string, ComplaintInfo> _complaints = new()
    {
        ["CMP-1"] = new ComplaintInfo
        {
            OrderId    = "ORD-1",
            CustomerId = "CUST-1990",
            Complaint  = "Arızalı bir ürün teslim aldım.",
            Status     = WellKnown.ComplaintStatuses.Resolved
        },
        ["CMP-2"] = new ComplaintInfo
        {
            OrderId    = "ORD-2",
            CustomerId = "CUST-1990",
            Complaint  = "Siparişim gecikti.",
            Status     = WellKnown.ComplaintStatuses.Pending
        }
    };

    private int _counter = 2;

    public string Create(ComplaintInfo complaint)
    {
        var id = $"CMP-{Interlocked.Increment(ref _counter)}";
        _complaints[id] = complaint;
        return id;
    }

    public ComplaintInfo? Get(string complaintId) =>
        _complaints.TryGetValue(complaintId, out var c) ? c : null;

    public IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByOrder(string orderId) =>
        _complaints
            .Where(kv => kv.Value.OrderId == orderId)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public IReadOnlyList<(string ComplaintId, ComplaintInfo Complaint)> GetByCustomer(string customerId) =>
        _complaints
            .Where(kv => kv.Value.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
}
