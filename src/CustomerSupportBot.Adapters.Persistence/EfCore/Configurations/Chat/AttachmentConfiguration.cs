// EfCore/Configurations/Chat/AttachmentConfiguration.cs
// chat.attachments tablosu — oturum silinince fotoğraflar da silinir (cascade).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Chat;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<AttachmentEntity>
{
    public void Configure(EntityTypeBuilder<AttachmentEntity> builder)
    {
        builder.ToTable("attachments", Schemas.Chat);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasMaxLength(32);

        builder.Property(a => a.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.CustomerId)
            .HasColumnName("customer_id")
            .HasMaxLength(64);

        builder.Property(a => a.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.SizeBytes)
            .HasColumnName("size_bytes")
            .IsRequired();

        builder.Property(a => a.Data)
            .HasColumnName("data")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(a => a.Description)
            .HasColumnName("description");

        builder.Property(a => a.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamptz");

        builder.Property(a => a.ApprovalId)
            .HasColumnName("approval_id")
            .HasMaxLength(64);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(a => new { a.SessionId, a.CreatedAt })
            .HasDatabaseName("ix_attachments_session");

        builder.HasIndex(a => a.ApprovalId)
            .HasDatabaseName("ix_attachments_approval");

        // FK: cascade delete — oturum silinirse fotoğrafları da gitsin
        builder.HasOne<SessionEntity>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .HasConstraintName("fk_attachments_session")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
