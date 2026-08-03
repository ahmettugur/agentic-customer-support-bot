// Infrastructure/Persistence/Configurations/Knowledge/KnowledgeArticleConfiguration.cs
// knowledge.articles tablosu yapılandırması.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Knowledge;

internal sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticleEntity>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticleEntity> builder)
    {
        builder.ToTable("articles", Schemas.Knowledge);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(a => a.Category)
            .HasColumnName("category")
            .HasMaxLength(64);

        builder.Property(a => a.IsPublished)
            .HasColumnName("is_published")
            .IsRequired();

        builder.Property(a => a.IndexedChunkCount)
            .HasColumnName("indexed_chunk_count")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(128);

        builder.HasIndex(a => a.IsPublished)
            .HasDatabaseName("ix_articles_is_published");

        builder.HasIndex(a => a.UpdatedAt)
            .HasDatabaseName("ix_articles_updated_at")
            .IsDescending();
    }
}
