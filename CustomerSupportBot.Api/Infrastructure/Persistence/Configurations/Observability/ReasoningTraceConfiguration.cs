// Infrastructure/Persistence/Configurations/Observability/ReasoningTraceConfiguration.cs
// observability.reasoning_traces tablosu — Workflow trace'leri.
// İç içe nesneler JSONB string olarak; mapper SerDe yapar.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Configurations.Observability;

internal sealed class ReasoningTraceConfiguration : IEntityTypeConfiguration<ReasoningTraceEntity>
{
    public void Configure(EntityTypeBuilder<ReasoningTraceEntity> builder)
    {
        builder.ToTable("reasoning_traces", Schemas.Observability);

        builder.HasKey(t => t.TraceId);

        builder.Property(t => t.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.UserQuery)
            .HasColumnName("user_query")
            .IsRequired();

        builder.Property(t => t.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamptz");

        builder.Property(t => t.TerminationReason)
            .HasColumnName("termination_reason")
            .HasMaxLength(128);

        builder.Property(t => t.FinalResponse)
            .HasColumnName("final_response");

        builder.Property(t => t.IterationCount)
            .HasColumnName("iteration_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.Error)
            .HasColumnName("error");

        builder.Property(t => t.EstimatedTokens)
            .HasColumnName("estimated_tokens")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(t => t.FirstDraftResponse)
            .HasColumnName("first_draft_response");

        builder.Property(t => t.WasRevised)
            .HasColumnName("was_revised")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(t => t.ReasoningJson)
            .HasColumnName("reasoning")
            .HasColumnType("jsonb");

        builder.Property(t => t.PlanningJson)
            .HasColumnName("planning")
            .HasColumnType("jsonb");

        builder.Property(t => t.SpecialistReasoningsJson)
            .HasColumnName("specialist_reasonings")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.FinalCritiqueJson)
            .HasColumnName("final_critique")
            .HasColumnType("jsonb");

        builder.Property(t => t.AgentVisitsJson)
            .HasColumnName("agent_visits")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.ToolCallsJson)
            .HasColumnName("tool_calls")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(t => new { t.SessionId, t.StartedAt })
            .HasDatabaseName("ix_traces_session_started");

        builder.HasIndex(t => t.StartedAt)
            .HasDatabaseName("ix_traces_started")
            .IsDescending();
    }
}
