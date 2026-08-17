// Infrastructure/Persistence/Configurations/Chat/ChatSessionModeConfiguration.cs
// chat.session_modes tablosu — Live Takeover Bot/Human kip kayıtları.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Chat;

internal sealed class ChatSessionModeConfiguration : IEntityTypeConfiguration<ChatSessionModeEntity>
{
    public void Configure(EntityTypeBuilder<ChatSessionModeEntity> builder)
    {
        builder.ToTable("session_modes", Schemas.Chat);

        builder.HasKey(m => m.SessionId);

        builder.Property(m => m.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.Mode)
            .HasColumnName("mode")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(m => m.HumanAgent)
            .HasColumnName("human_agent")
            .HasMaxLength(128);

        builder.Property(m => m.EnteredAt)
            .HasColumnName("entered_at")
            .HasColumnType("timestamptz");

        builder.Property(m => m.LastActivityAt)
            .HasColumnName("last_activity_at")
            .HasColumnType("timestamptz");

        builder.Property(m => m.MessageCount)
            .HasColumnName("message_count")
            .HasDefaultValue(0)
            .IsRequired();

        // Sadece Human modlu kayıtlar — GetActive() partial index
        builder.HasIndex(m => m.Mode)
            .HasDatabaseName("ix_session_modes_active")
            .HasFilter("mode = 'Human'");
    }
}
