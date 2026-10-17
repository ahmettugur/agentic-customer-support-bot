// observability.workflow_checkpoints — bkz. WorkflowCheckpointEntity. Tur silinince checkpoint'leri de gider (cascade).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Observability;

internal sealed class WorkflowCheckpointConfiguration : IEntityTypeConfiguration<WorkflowCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowCheckpointEntity> builder)
    {
        builder.ToTable("workflow_checkpoints", Schemas.Observability);
        builder.HasKey(c => new { c.TraceId, c.CheckpointId });

        builder.Property(c => c.TraceId).HasColumnName("trace_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.CheckpointId).HasColumnName("checkpoint_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.ParentCheckpointId).HasColumnName("parent_checkpoint_id").HasMaxLength(64);
        // "text", jsonb DEĞİL: jsonb nesne anahtarlarını yeniden sıralar ve MAF'ın polimorfik JSON'unda "$type" ilk üye
        // olmak zorundadır — jsonb ile devam "metadata property is not the first property" hatasıyla çöker (test edildi).
        builder.Property(c => c.PayloadJson).HasColumnName("payload").HasColumnType("text").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<WorkflowTurnEntity>()
            .WithMany()
            .HasForeignKey(c => c.TraceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TraceId, c.CreatedAt }).HasDatabaseName("ix_workflow_checkpoints_trace_created");
    }
}
