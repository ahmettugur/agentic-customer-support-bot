// EfCore/Configurations/Notifications/NotificationLogConfiguration.cs

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Notifications;

internal sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLogEntity>
{
    public void Configure(EntityTypeBuilder<NotificationLogEntity> builder)
    {
        builder.ToTable("sent_log", Schemas.Notifications);

        builder.HasKey(n => n.Key);

        builder.Property(n => n.Key)
            .HasColumnName("key")
            .HasMaxLength(200);

        builder.Property(n => n.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
