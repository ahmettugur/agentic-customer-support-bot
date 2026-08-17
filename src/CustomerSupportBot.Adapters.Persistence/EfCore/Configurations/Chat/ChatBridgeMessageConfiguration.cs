// Infrastructure/Persistence/Configurations/Chat/ChatBridgeMessageConfiguration.cs
// chat.bridge_messages tablosu — Live Takeover history (User/Bot/Admin/System).
// BotTyping persist edilmez (transient).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Chat;

internal sealed class ChatBridgeMessageConfiguration : IEntityTypeConfiguration<ChatBridgeMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChatBridgeMessageEntity> builder)
    {
        builder.ToTable("bridge_messages", Schemas.Chat);

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(m => m.MessageId)
            .HasColumnName("message_id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.Sender)
            .HasColumnName("sender")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(m => m.HumanAgent)
            .HasColumnName("human_agent")
            .HasMaxLength(128);

        builder.Property(m => m.Text)
            .HasColumnName("text")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(m => new { m.SessionId, m.Id })
            .HasDatabaseName("ix_bridge_messages_session");

        builder.HasIndex(m => m.MessageId)
            .HasDatabaseName("ix_bridge_messages_message_id");
    }
}
