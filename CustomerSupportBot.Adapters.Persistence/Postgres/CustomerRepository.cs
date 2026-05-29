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
}
