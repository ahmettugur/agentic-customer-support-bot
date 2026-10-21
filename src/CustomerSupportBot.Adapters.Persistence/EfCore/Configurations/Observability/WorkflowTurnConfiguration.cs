// observability.workflow_turns — bkz. WorkflowTurnEntity.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Observability;

internal sealed class WorkflowTurnConfiguration : IEntityTypeConfiguration<WorkflowTurnEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowTurnEntity> builder)
    {
        builder.ToTable("workflow_turns", Schemas.Observability);
        builder.HasKey(t => t.TraceId);

        builder.Property(t => t.TraceId).HasColumnName("trace_id").HasMaxLength(64).IsRequired();
        builder.Property(t => t.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(t => t.Query).HasColumnName("query").IsRequired();
        builder.Property(t => t.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
        builder.Property(t => t.ReasoningJson).HasColumnName("reasoning").HasColumnType("jsonb");
        builder.Property(t => t.HistoryCount).HasColumnName("history_count").IsRequired();
        builder.Property(t => t.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(t => t.StartedAt).HasDatabaseName("ix_workflow_turns_started_at");
    }
}
