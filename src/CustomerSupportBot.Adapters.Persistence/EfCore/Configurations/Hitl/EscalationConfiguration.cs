// Infrastructure/Persistence/Configurations/Hitl/EscalationConfiguration.cs
// hitl.escalations tablosu — needs_escalation status'tan türeyen eskalasyonlar.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;

internal sealed class EscalationConfiguration : IEntityTypeConfiguration<EscalationEntity>
{
    public void Configure(EntityTypeBuilder<EscalationEntity> builder)
    {
        builder.ToTable("escalations", Schemas.Hitl);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64);

        builder.Property(e => e.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64);

        builder.Property(e => e.AgentName)
            .HasColumnName("agent_name")
            .HasMaxLength(64);

        builder.Property(e => e.UserQuery)
            .HasColumnName("user_query")
            .IsRequired();

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.Property(e => e.MissingContextJson)
            .HasColumnName("missing_context")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ResponseSummary)
            .HasColumnName("response_summary");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.AcknowledgedAt)
            .HasColumnName("acknowledged_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.AssignedTo)
            .HasColumnName("assigned_to")
            .HasMaxLength(128);

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution");

        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_escalations_status_created_at");

        // Session içi dedup için partial index — açık eskalasyonları hızlı bul
        builder.HasIndex(e => e.SessionId)
            .HasDatabaseName("ix_escalations_session_open")
            .HasFilter("status IN ('Open', 'Acknowledged')");
    }
}
