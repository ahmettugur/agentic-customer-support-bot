// Infrastructure/Persistence/Configurations/Hitl/ApprovalRequestConfiguration.cs
// hitl.approval_requests tablosu — HITL onay kayıtları.
// TaskCompletionSource persist EDİLMEZ — sadece in-memory.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequestEntity>
{
    public void Configure(EntityTypeBuilder<ApprovalRequestEntity> builder)
    {
        builder.ToTable("approval_requests", Schemas.Hitl);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64);

        builder.Property(a => a.CustomerId)
            .HasColumnName("customer_id")
            .HasMaxLength(32);

        builder.Property(a => a.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64);

        builder.Property(a => a.ToolName)
            .HasColumnName("tool_name")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.AgentName)
            .HasColumnName("agent_name")
            .HasMaxLength(64);

        builder.Property(a => a.ParametersJson)
            .HasColumnName("parameters")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.ParamSignature)
            .HasColumnName("param_signature")
            .HasMaxLength(1000);

        builder.Property(a => a.UserQuery)
            .HasColumnName("user_query");

        builder.Property(a => a.Justification)
            .HasColumnName("justification");

        builder.Property(a => a.RequestedAt)
            .HasColumnName("requested_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("timestamptz");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.DecidedBy)
            .HasColumnName("decided_by")
            .HasMaxLength(128);

        builder.Property(a => a.DecisionReason)
            .HasColumnName("decision_reason");

        builder.Property(a => a.TimeoutSeconds)
            .HasColumnName("timeout_seconds")
            .IsRequired();

        builder.Property(a => a.ExecutionResult)
            .HasColumnName("execution_result");

        builder.Property(a => a.ExecutedAt)
            .HasColumnName("executed_at")
            .HasColumnType("timestamptz");

        builder.Property(a => a.ExecutionStatus)
            .HasColumnName("execution_status")
            .HasMaxLength(16)
            .HasDefaultValue("None")
            .IsRequired();

        builder.Property(a => a.CustomerSeenAt)
            .HasColumnName("customer_seen_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(a => new { a.Status, a.RequestedAt })
            .HasDatabaseName("ix_approvals_status_requested_at");

        builder.HasIndex(a => a.SessionId)
            .HasDatabaseName("ix_approvals_session_id");

        builder.HasIndex(a => a.CustomerId)
            .HasDatabaseName("ix_approvals_customer_id");

        // "Askıda kalmış yürütme" sorgusu (Approved + Running) için — admin panelinin
        // dikkat çekmesi gereken kayıtlar bunlar.
        builder.HasIndex(a => new { a.Status, a.ExecutionStatus })
            .HasDatabaseName("ix_approvals_status_execution_status");

        // Mükerrer onay talebi oluşumunu DB seviyesinde İMKANSIZ kılar (bkz.
        // ApprovalRequest.ParamSignature XML dokümanı) — yalnızca bekleyen (Pending) satırlar
        // üzerinde kısmi (partial) bir unique index: karara bağlanmış eski kayıtlar veya
        // ParamSignature'ı null olan (SessionId'siz/kimliksiz akış) satırlar kısıta girmez.
        builder.HasIndex(a => new { a.SessionId, a.ToolName, a.ParamSignature })
            .HasDatabaseName("ux_approvals_pending_dedup")
            .IsUnique()
            .HasFilter("status = 'Pending' AND param_signature IS NOT NULL");
    }
}
