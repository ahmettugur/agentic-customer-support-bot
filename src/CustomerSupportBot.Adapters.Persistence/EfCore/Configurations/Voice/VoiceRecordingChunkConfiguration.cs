// voice.recording_chunks — (call, track, sequence) benzersiz: aynı parça iki kez yüklenmez.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Voice;

internal sealed class VoiceRecordingChunkConfiguration : IEntityTypeConfiguration<VoiceRecordingChunkEntity>
{
    public void Configure(EntityTypeBuilder<VoiceRecordingChunkEntity> builder)
    {
        builder.ToTable("recording_chunks", Schemas.Voice);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(c => c.CallId).HasColumnName("call_id").HasMaxLength(32).IsRequired();
        builder.Property(c => c.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.Track).HasColumnName("track").HasMaxLength(16).IsRequired();
        builder.Property(c => c.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(c => c.OffsetMs).HasColumnName("offset_ms").IsRequired();
        builder.Property(c => c.DurationMs).HasColumnName("duration_ms").IsRequired();
        builder.Property(c => c.ContentType).HasColumnName("content_type").HasMaxLength(64).IsRequired();
        builder.Property(c => c.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(c => c.Data).HasColumnName("data").HasColumnType("bytea").IsRequired();
        builder.Property(c => c.TranscriptStatus).HasColumnName("transcript_status").HasMaxLength(16).IsRequired();
        builder.Property(c => c.TranscriptText).HasColumnName("transcript_text");
        builder.Property(c => c.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(c => c.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.ClaimedAt).HasColumnName("claimed_at").HasColumnType("timestamptz");
        builder.Property(c => c.AudioPurgedAt).HasColumnName("audio_purged_at").HasColumnType("timestamptz");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(c => new { c.CallId, c.Track, c.Sequence }).IsUnique().HasDatabaseName("ux_chunks_call_track_seq");
        builder.HasIndex(c => new { c.TranscriptStatus, c.NextAttemptAt }).HasDatabaseName("ix_chunks_transcript_queue");
        builder.HasIndex(c => c.CreatedAt).HasDatabaseName("ix_chunks_created");

        builder.HasOne<VoiceCallEntity>().WithMany().HasForeignKey(c => c.CallId)
            .HasConstraintName("fk_chunks_call").OnDelete(DeleteBehavior.Cascade);
    }
}
