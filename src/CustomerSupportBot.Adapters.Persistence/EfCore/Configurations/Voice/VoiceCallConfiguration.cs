// voice.calls — temsilci ve oturum başına tek açık görüşme: partial unique index (çok pod'da da geçerli).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Voice;

internal sealed class VoiceCallConfiguration : IEntityTypeConfiguration<VoiceCallEntity>
{
    internal const string OpenFilter = "status IN ('Ringing','Active')";

    public void Configure(EntityTypeBuilder<VoiceCallEntity> builder)
    {
        builder.ToTable("calls", Schemas.Voice);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(c => c.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.AgentId).HasColumnName("agent_id").HasMaxLength(128).IsRequired();
        builder.Property(c => c.AgentDisplayName).HasColumnName("agent_display_name").HasMaxLength(128).IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.AnsweredAt).HasColumnName("answered_at").HasColumnType("timestamptz");
        builder.Property(c => c.ConsentAt).HasColumnName("consent_at").HasColumnType("timestamptz");
        builder.Property(c => c.EndedAt).HasColumnName("ended_at").HasColumnType("timestamptz");
        builder.Property(c => c.EndReason).HasColumnName("end_reason").HasMaxLength(32);
        builder.Property(c => c.LastChunkAt).HasColumnName("last_chunk_at").HasColumnType("timestamptz");

        builder.HasIndex(c => c.AgentId).IsUnique().HasFilter(OpenFilter).HasDatabaseName("ux_calls_agent_open");
        builder.HasIndex(c => c.SessionId).IsUnique().HasFilter(OpenFilter).HasDatabaseName("ux_calls_session_open");
        builder.HasIndex(c => new { c.SessionId, c.CreatedAt }).HasDatabaseName("ix_calls_session");

        builder.HasOne<SessionEntity>().WithMany().HasForeignKey(c => c.SessionId)
            .HasConstraintName("fk_calls_session").OnDelete(DeleteBehavior.Cascade);
    }
}
