// Infrastructure/Persistence/Configurations/Chat/SessionConfiguration.cs
// chat.sessions tablosu — AgentSession + SessionState (JSONB).

using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Configurations.Chat;

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

        builder.Property(s => s.StateJson)
            .HasColumnName("state")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(s => s.LastActivity)
            .HasDatabaseName("ix_sessions_last_activity")
            .IsDescending();
    }
}
