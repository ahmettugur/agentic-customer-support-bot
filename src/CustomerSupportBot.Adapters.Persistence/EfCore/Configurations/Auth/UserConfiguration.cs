// Infrastructure/Persistence/Configurations/Auth/UserConfiguration.cs

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Auth;

internal sealed class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("users", Schemas.Auth);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(u => u.Username)
            .HasColumnName("username")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(u => u.LinkedAgentId)
            .HasColumnName("linked_agent_id")
            .HasMaxLength(32);

        builder.Property(u => u.LinkedCustomerId)
            .HasColumnName("linked_customer_id")
            .HasMaxLength(32);

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(u => u.LastLoginAt)
            .HasColumnName("last_login_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(u => u.Username)
            .HasDatabaseName("ux_users_username")
            .IsUnique();

        // Bir müşteriye YALNIZCA BİR hesap. Kayıt akışı e-posta eşleşmesi istiyor ama tek
        // başına yetmez: e-postayı bilen biri, gerçek sahip kaydolmadan önce hesabı açabilir.
        // Bu kısıt ikinci bir hesabın sessizce eklenmesini engeller — çakışma DB seviyesinde
        // reddedilir ve durum görünür olur.
        //
        // Filtreli index: linked_customer_id çoğu kullanıcıda (admin/agent/partner) NULL'dur
        // ve Postgres'te NULL'lar unique kısıtı ihlal etmez; filtre bunu açık hâle getirir.
        builder.HasIndex(u => u.LinkedCustomerId)
            .HasDatabaseName("ux_users_linked_customer_id")
            .IsUnique()
            .HasFilter("linked_customer_id IS NOT NULL");

        // Default admin runtime'da PersistenceHydrator tarafından seed edilir
        // (BCrypt hash'i her ortamda runtime'da üretilir).
    }
}
