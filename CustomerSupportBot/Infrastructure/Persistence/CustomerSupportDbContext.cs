// Infrastructure/Persistence/CustomerSupportDbContext.cs
// EF Core 10 + Npgsql DbContext.
// Tüm IEntityTypeConfiguration sınıfları assembly'den otomatik uygulanır.

using CustomerSupportBot.Infrastructure.Persistence.Entities.Analytics;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Chat;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Hitl;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Observability;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Infrastructure.Persistence;

public sealed class CustomerSupportDbContext : DbContext
{
    public CustomerSupportDbContext(DbContextOptions<CustomerSupportDbContext> options)
        : base(options)
    {
    }

    // ─── chat schema ───
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ChatSessionModeEntity> ChatSessionModes => Set<ChatSessionModeEntity>();
    public DbSet<ChatBridgeMessageEntity> ChatBridgeMessages => Set<ChatBridgeMessageEntity>();

    // ─── hitl schema ───
    public DbSet<ApprovalRequestEntity> Approvals => Set<ApprovalRequestEntity>();
    public DbSet<EscalationEntity> Escalations => Set<EscalationEntity>();

    // ─── observability schema ───
    public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();

    // ─── analytics schema ───
    public DbSet<RatingEntity> Ratings => Set<RatingEntity>();

    // ─── auth schema ───
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerSupportDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
