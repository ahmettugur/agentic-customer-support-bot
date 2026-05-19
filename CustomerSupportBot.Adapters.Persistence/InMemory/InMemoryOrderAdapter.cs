// Adapters.Persistence/InMemory/InMemoryOrderAdapter.cs
// DRIVEN ADAPTER — IOrderRepository → InMemory (FakeDatabase.OrdersDb sarar).

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

/// <summary>
/// FakeDatabase sipariş deposunu IOrderRepository port'una bağlar.
/// </summary>
public sealed class InMemoryOrderAdapter : IOrderRepository
{
    private readonly ConcurrentDictionary<string, OrderInfo> _orders = new()
    {
        ["ORD-1"] = new OrderInfo
        {
            Product    = "Dell XPS 15",
            Quantity   = 1,
            CustomerId = "CUST-1990",
            Status     = WellKnown.OrderStatuses.Shipped,
            OrderDate  = new DateTime(2024, 1, 15, 10, 30, 0)
        },
        ["ORD-2"] = new OrderInfo
        {
            Product    = "Apple iPhone 15 Pro",
            Quantity   = 2,
            CustomerId = "CUST-1990",
            Status     = WellKnown.OrderStatuses.Delivered,
            OrderDate  = new DateTime(2024, 2, 20, 14, 45, 0)
        }
    };

    private int _counter = 2;

    public string Create(OrderInfo order)
    {
        var id = $"ORD-{Interlocked.Increment(ref _counter)}";
        _orders[id] = order;
        return id;
    }

    public OrderInfo? Get(string orderId) =>
        _orders.TryGetValue(orderId, out var o) ? o : null;

    public IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId) =>
        _orders
            .Where(kv => kv.Value.CustomerId == customerId)
            .OrderByDescending(kv => kv.Value.OrderDate)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public (string OrderId, OrderInfo Order)? GetLast(string customerId)
    {
        var entry = _orders
            .Where(kv => kv.Value.CustomerId == customerId)
            .OrderByDescending(kv => kv.Value.OrderDate)
            .FirstOrDefault();

        return entry.Key != null ? (entry.Key, entry.Value) : null;
    }
}
