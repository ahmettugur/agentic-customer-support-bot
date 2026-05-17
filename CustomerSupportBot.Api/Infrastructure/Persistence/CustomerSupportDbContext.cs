// Infrastructure/Persistence/CustomerSupportDbContext.cs
// EF Core 10 + Npgsql DbContext.
// Tüm IEntityTypeConfiguration sınıfları assembly'den otomatik uygulanır.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Analytics;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Chat;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Hitl;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Improvement;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Observability;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Personalization;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Workflow;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.Infrastructure.Persistence;

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

    // ─── analytics schema ───
    public DbSet<RatingEntity> Ratings => Set<RatingEntity>();

    // ─── auth schema ───
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    // ─── personalization schema ───
    public DbSet<CustomerProfileEntity> CustomerProfiles => Set<CustomerProfileEntity>();

    // ─── improvement schema ───
    public DbSet<LessonEntity> Lessons => Set<LessonEntity>();

    // ─── workflow schema ───
    public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();

    // ─── analytics schema (sla) ───
    public DbSet<SlaEventEntity> SlaEvents => Set<SlaEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerSupportDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
