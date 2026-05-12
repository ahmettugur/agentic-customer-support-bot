// Infrastructure/Persistence/Configurations/Auth/RefreshTokenConfiguration.cs

using CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Infrastructure.Persistence.Configurations.Auth;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
{
    public void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
    {
        builder.ToTable("refresh_tokens", Schemas.Auth);

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamptz");

        builder.Property(t => t.ReplacedByTokenHash)
            .HasColumnName("replaced_by_token_hash")
            .HasMaxLength(128);

        builder.HasIndex(t => t.TokenHash)
            .HasDatabaseName("ux_refresh_tokens_hash")
            .IsUnique();

        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("ix_refresh_tokens_user");

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_refresh_tokens_user")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
