// Infrastructure/Persistence/Configurations/Hitl/HumanAgentConfiguration.cs
// hitl.human_agents tablosu — skills-based routing temsilci kayıtları.

using CustomerSupportBot.Infrastructure.Persistence.Entities.Hitl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Infrastructure.Persistence.Configurations.Hitl;

internal sealed class HumanAgentConfiguration : IEntityTypeConfiguration<HumanAgentEntity>
{
    public void Configure(EntityTypeBuilder<HumanAgentEntity> builder)
    {
        builder.ToTable("human_agents", Schemas.Hitl);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(256);

        builder.Property(e => e.SkillsJson)
            .HasColumnName("skills")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.LanguagesJson)
            .HasColumnName("languages")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(e => e.MaxConcurrentLoad)
            .HasColumnName("max_concurrent_load")
            .IsRequired();

        builder.Property(e => e.CurrentLoad)
            .HasColumnName("current_load")
            .IsRequired();

        builder.Property(e => e.Priority)
            .HasColumnName("priority")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.LastAssignedAt)
            .HasColumnName("last_assigned_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("ix_human_agents_active")
            .HasFilter("is_active = true");
    }
}
