// Infrastructure/Persistence/Configurations/Improvement/LessonConfiguration.cs
// improvement.lessons tablosu yapılandırması.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Improvement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Improvement;

internal sealed class LessonConfiguration : IEntityTypeConfiguration<LessonEntity>
{
    public void Configure(EntityTypeBuilder<LessonEntity> builder)
    {
        builder.ToTable("lessons", Schemas.Improvement);

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(l => l.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(l => l.LessonText)
            .HasColumnName("lesson_text")
            .IsRequired();

        builder.Property(l => l.Observation)
            .HasColumnName("observation")
            .IsRequired();

        builder.Property(l => l.SuggestedAgent)
            .HasColumnName("suggested_agent")
            .HasMaxLength(128);

        builder.Property(l => l.SourceTraceIdsJson)
            .HasColumnName("source_trace_ids")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(l => l.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(l => l.DecidedBy)
            .HasColumnName("decided_by")
            .HasMaxLength(128);

        builder.Property(l => l.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("timestamptz");

        builder.Property(l => l.DecisionReason)
            .HasColumnName("decision_reason");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(l => l.VectorMemoryId)
            .HasColumnName("vector_memory_id")
            .HasMaxLength(128);

        builder.HasIndex(l => l.Status)
            .HasDatabaseName("ix_lessons_status");

        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("ix_lessons_created_at")
            .IsDescending();
    }
}
