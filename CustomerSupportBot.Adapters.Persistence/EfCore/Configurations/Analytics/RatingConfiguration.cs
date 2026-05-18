// Infrastructure/Persistence/Configurations/Analytics/RatingConfiguration.cs
// analytics.ratings tablosu — Konuşma değerlendirmeleri (session başına tek rating).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Analytics;

internal sealed class RatingConfiguration : IEntityTypeConfiguration<RatingEntity>
{
    public void Configure(EntityTypeBuilder<RatingEntity> builder)
    {
        builder.ToTable("ratings", Schemas.Analytics);

        builder.HasKey(r => r.SessionId);

        builder.Property(r => r.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.Stars)
            .HasColumnName("stars")
            .IsRequired();

        builder.Property(r => r.Feedback)
            .HasColumnName("feedback");

        builder.Property(r => r.RatedAt)
            .HasColumnName("rated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(r => r.RatedAt)
            .HasDatabaseName("ix_ratings_rated_at")
            .IsDescending();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ratings_stars_range", "stars BETWEEN 1 AND 5"));
    }
}
