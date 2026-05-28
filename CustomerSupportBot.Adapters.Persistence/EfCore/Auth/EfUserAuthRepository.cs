// Adapters.Persistence/EfCore/Auth/EfUserAuthRepository.cs
// IUserAuthRepository → EF Core implementation.

using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Auth;

public sealed class EfUserAuthRepository : IUserAuthRepository
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;

    public EfUserAuthRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<UserInfo?> FindActiveByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await ctx.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<UserInfo?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await ctx.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task UpdateLastLoginAsync(string id, DateTime lastLoginAt, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await ctx.Users.FirstAsync(u => u.Id == id, ct);
        entity.LastLoginAt = lastLoginAt;
        await ctx.SaveChangesAsync(ct);
    }

    private static UserInfo Map(Entities.Auth.UserEntity e) =>
        new(e.Id, e.Username, e.PasswordHash, e.Role, e.LinkedAgentId, e.IsActive, e.CreatedAt, e.LastLoginAt);
}
