// Infrastructure/Persistence/Configurations/Workflow/WorkflowDefinitionConfiguration.cs
// workflow.workflow_definitions tablosu yapılandırması.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Workflow;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionEntity> builder)
    {
        builder.ToTable("workflow_definitions", Schemas.Workflow);

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(w => w.Description)
            .HasColumnName("description")
            .IsRequired();

        builder.Property(w => w.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(w => w.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(w => w.TriggerKeywordsJson)
            .HasColumnName("trigger_keywords")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(w => w.InputPatternsJson)
            .HasColumnName("input_patterns")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(w => w.StepsJson)
            .HasColumnName("steps")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(w => w.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(128);

        builder.HasIndex(w => w.IsActive)
            .HasDatabaseName("ix_workflow_definitions_is_active");
    }
}
