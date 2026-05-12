// Infrastructure/Persistence/Configurations/Chat/MessageConfiguration.cs
// chat.messages tablosu — sıralı user/assistant mesaj geçmişi.

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Configurations.Chat;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        builder.ToTable("messages", Schemas.Chat);

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(m => m.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(m => m.Text)
            .HasColumnName("text")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(m => new { m.SessionId, m.Id })
            .HasDatabaseName("ix_messages_session");

        // FK: cascade delete — session silinirse mesajları da gitsin
        builder.HasOne<SessionEntity>()
            .WithMany()
            .HasForeignKey(m => m.SessionId)
            .HasConstraintName("fk_messages_session")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
