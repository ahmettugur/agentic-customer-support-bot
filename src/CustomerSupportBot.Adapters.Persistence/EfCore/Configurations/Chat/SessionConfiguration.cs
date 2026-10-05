// Infrastructure/Persistence/Configurations/Chat/SessionConfiguration.cs
// chat.sessions tablosu — AgentSession + SessionState (JSONB).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Chat;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<SessionEntity>
{
    public void Configure(EntityTypeBuilder<SessionEntity> builder)
    {
        builder.ToTable("sessions", Schemas.Chat);

        builder.HasKey(s => s.SessionId);

        builder.Property(s => s.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(s => s.LastActivity)
            .HasColumnName("last_activity")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Eşzamanlılık belirteci: state güncellemesi "UPDATE … WHERE state = <okunan>" olur — araya başka bir
        // yazıcı girdiyse 0 satır ve DbUpdateConcurrencyException (bkz. PostgresSessionManager.WriteStateAsync).
        builder.Property(s => s.StateJson)
            .HasColumnName("state")
            .HasColumnType("jsonb")
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(s => s.LastActivity)
            .HasDatabaseName("ix_sessions_last_activity")
            .IsDescending();
    }
}
