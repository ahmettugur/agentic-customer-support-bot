// Adapters.Persistence/EfCore/Auth/EfRefreshTokenRepository.cs
// IRefreshTokenRepository → EF Core implementation.

using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Auth;

public sealed class EfRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;

    public EfRefreshTokenRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task CreateAsync(string id, string userId, string tokenHash,
        DateTime expiresAt, DateTime createdAt, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await ctx.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task RevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await ctx.RefreshTokens.FirstAsync(t => t.Id == id, ct);
        entity.RevokedAt = revokedAt;
        entity.ReplacedByTokenHash = replacedByTokenHash;
        await ctx.SaveChangesAsync(ct);
    }

    private static RefreshTokenInfo Map(RefreshTokenEntity e) =>
        new(e.Id, e.UserId, e.TokenHash, e.ExpiresAt, e.CreatedAt, e.RevokedAt, e.ReplacedByTokenHash);
}
