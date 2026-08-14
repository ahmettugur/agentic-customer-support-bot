using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;

    public CustomerRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public bool Exists(long customerId)
    {
        using var ctx = _dbFactory.CreateDbContext();
        return ctx.Customers.Any(c => c.Id == customerId);
    }

    public async Task<string?> GetFullNameAsync(long customerId, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        return await ctx.Customers
            .Where(c => c.Id == customerId)
            .Select(c => c.FullName)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, string>> GetFullNamesAsync(
        IReadOnlyCollection<long> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return new Dictionary<long, string>();

        // Distinct: aynı müşteri kuyrukta birden çok istekle yer alabilir.
        var ids = customerIds.Distinct().ToArray();

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        return await ctx.Customers
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName })
            .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
    }
}
