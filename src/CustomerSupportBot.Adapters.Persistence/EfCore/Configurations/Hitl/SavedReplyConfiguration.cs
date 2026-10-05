// EfCore/Configurations/Hitl/SavedReplyConfiguration.cs

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;

internal sealed class SavedReplyConfiguration : IEntityTypeConfiguration<SavedReplyEntity>
{
    public const string ShortcutIndexName = "ux_saved_replies_shortcut";

    public void Configure(EntityTypeBuilder<SavedReplyEntity> builder)
    {
        builder.ToTable("saved_replies", Schemas.Hitl);
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(r => r.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        builder.Property(r => r.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(r => r.Shortcut).HasColumnName("shortcut").HasMaxLength(40);
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // Kısayol serviste küçük harfe normalleştirilir; benzersizlik burada garanti edilir (eşzamanlı ekleme).
        // NULL'lar çakışmaz — kısayolsuz yanıtlar serbest.
        builder.HasIndex(r => r.Shortcut).IsUnique().HasDatabaseName(ShortcutIndexName);
    }
}
