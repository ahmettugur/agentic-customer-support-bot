// EfCore/Configurations/Chat/ConversationDispositionConfiguration.cs

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Chat;

internal sealed class ConversationDispositionConfiguration : IEntityTypeConfiguration<ConversationDispositionEntity>
{
    public void Configure(EntityTypeBuilder<ConversationDispositionEntity> builder)
    {
        builder.ToTable("conversation_dispositions", Schemas.Chat);
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id").HasMaxLength(32);
        // Oturuma FK yok: kayıt KVKK silmesinde ISessionDataEraser ile silinir (puanlar gibi).
        builder.Property(d => d.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(d => d.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();
        // text[] — en sık etiketler veritabanında unnest ile sayılır.
        builder.Property(d => d.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        builder.Property(d => d.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(d => d.ClosedBy).HasColumnName("closed_by").HasMaxLength(100);
        builder.Property(d => d.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(d => d.SessionId).HasDatabaseName("ix_conversation_dispositions_session");
    }
}
