// Infrastructure/Persistence/CustomerSupportDbContext.cs
// EF Core 10 + Npgsql DbContext.
// Tüm IEntityTypeConfiguration sınıfları assembly'den otomatik uygulanır.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Improvement;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Personalization;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

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
    public DbSet<HumanAgentEntity> HumanAgents => Set<HumanAgentEntity>();

    // ─── observability schema ───
    public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();
    public DbSet<LlmCallUsageEntity> LlmCallUsages => Set<LlmCallUsageEntity>();

    // ─── analytics schema ───
    public DbSet<RatingEntity> Ratings => Set<RatingEntity>();

    // ─── auth schema ───
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    // ─── personalization schema ───
    public DbSet<CustomerProfileEntity> CustomerProfiles => Set<CustomerProfileEntity>();

    // ─── improvement schema ───
    public DbSet<LessonEntity> Lessons => Set<LessonEntity>();

    // ─── analytics schema (sla) ───
    public DbSet<SlaEventEntity> SlaEvents => Set<SlaEventEntity>();

    // ─── catalog schema ───
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderDetailEntity> OrderDetails => Set<OrderDetailEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<ComplaintEntity> Complaints => Set<ComplaintEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerSupportDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
