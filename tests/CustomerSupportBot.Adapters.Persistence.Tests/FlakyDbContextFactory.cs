using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
// Tests/Helpers/FlakyDbContextFactory.cs
// İlk N çağrıyı hatayla başarısız kılan, sonrasında gerçek factory'ye devreden
// sarmalayıcı — Postgres adaptörlerindeki hydration-retry testlerinde geçici DB
// kesintisini simüle etmek için kullanılır.

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public sealed class FlakyDbContextFactory : IDbContextFactory<CustomerSupportDbContext>
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _inner;
    private int _failuresRemaining;

    public FlakyDbContextFactory(IDbContextFactory<CustomerSupportDbContext> inner, int failuresRemaining)
    {
        _inner = inner;
        _failuresRemaining = failuresRemaining;
    }

    public CustomerSupportDbContext CreateDbContext()
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
            throw new InvalidOperationException("simulated transient DB failure");
        return _inner.CreateDbContext();
    }

    public Task<CustomerSupportDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
            throw new InvalidOperationException("simulated transient DB failure");
        return _inner.CreateDbContextAsync(ct);
    }
}
