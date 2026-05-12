// Services/Personalization/InMemoryCustomerProfileStore.cs
// Per-customer kişiselleştirme profilinin in-memory deposu.
// Production'da Postgres / Redis ile değiştirilebilir — interface aynı kalır.

using System.Collections.Concurrent;
using CustomerSupportBot.Api.Models.Memory;

namespace CustomerSupportBot.Api.Services.Personalization;

public sealed class InMemoryCustomerProfileStore : ICustomerProfileStore
{
    private readonly ConcurrentDictionary<string, CustomerProfile> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    public CustomerProfile? Get(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return null;
        return _byId.TryGetValue(customerId, out var p) ? p : null;
    }

    public CustomerProfile GetOrCreate(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("customerId boş olamaz", nameof(customerId));

        return _byId.GetOrAdd(customerId, id => new CustomerProfile { CustomerId = id });
    }

    public void Upsert(CustomerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.CustomerId))
            throw new ArgumentException("CustomerId boş olamaz", nameof(profile));

        profile.LastInteractionAt = profile.LastInteractionAt == default
            ? DateTime.UtcNow
            : profile.LastInteractionAt;

        _byId[profile.CustomerId] = profile;
    }

    public bool Delete(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return false;
        return _byId.TryRemove(customerId, out _);
    }

    public IReadOnlyList<CustomerProfile> List(int take = 100)
    {
        if (take <= 0) take = 100;
        return _byId.Values
            .OrderByDescending(p => p.LastInteractionAt)
            .Take(take)
            .ToList();
    }

    public int Count => _byId.Count;
}
