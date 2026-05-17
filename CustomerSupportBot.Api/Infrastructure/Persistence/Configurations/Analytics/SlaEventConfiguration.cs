// Infrastructure/Persistence/Configurations/Analytics/SlaEventConfiguration.cs
// analytics.sla_events tablosu yapılandırması.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Configurations.Analytics;

internal sealed class SlaEventConfiguration : IEntityTypeConfiguration<SlaEventEntity>
{
    public void Configure(EntityTypeBuilder<SlaEventEntity> builder)
    {
        builder.ToTable("sla_events", Schemas.Analytics);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Timestamp)
            .HasColumnName("timestamp")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.TargetId)
            .HasColumnName("target_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.AgeSeconds)
            .HasColumnName("age_seconds")
            .IsRequired();

        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasMaxLength(64);

        builder.Property(e => e.Note)
            .HasColumnName("note");

        builder.HasIndex(e => e.Timestamp)
            .HasDatabaseName("ix_sla_events_timestamp")
            .IsDescending();

        builder.HasIndex(e => new { e.Kind, e.TargetId, e.Severity })
            .HasDatabaseName("ix_sla_events_kind_target_severity");
    }
}
