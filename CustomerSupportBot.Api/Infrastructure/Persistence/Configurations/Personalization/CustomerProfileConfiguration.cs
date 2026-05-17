// Infrastructure/Persistence/Configurations/Personalization/CustomerProfileConfiguration.cs
// personalization.customer_profiles tablosu yapılandırması.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Personalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Configurations.Personalization;

internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfileEntity>
{
    public void Configure(EntityTypeBuilder<CustomerProfileEntity> builder)
    {
        builder.ToTable("customer_profiles", Schemas.Personalization);

        builder.HasKey(p => p.CustomerId);

        builder.Property(p => p.CustomerId)
            .HasColumnName("customer_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.PreferredLanguage)
            .HasColumnName("preferred_language")
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(p => p.PreferredTone)
            .HasColumnName("preferred_tone")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.IntentFrequencyJson)
            .HasColumnName("intent_frequency")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(p => p.ProductInterestsJson)
            .HasColumnName("product_interests")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(p => p.RecentRatingsJson)
            .HasColumnName("recent_ratings")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(p => p.Summary)
            .HasColumnName("summary");

        builder.Property(p => p.AdminNote)
            .HasColumnName("admin_note");

        builder.Property(p => p.TotalSessions)
            .HasColumnName("total_sessions")
            .IsRequired();

        builder.Property(p => p.TotalTurns)
            .HasColumnName("total_turns")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(p => p.LastInteractionAt)
            .HasColumnName("last_interaction_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(p => p.LastConsolidatedAt)
            .HasColumnName("last_consolidated_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(p => p.LastInteractionAt)
            .HasDatabaseName("ix_customer_profiles_last_interaction_at")
            .IsDescending();
    }
}
