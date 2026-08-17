// EfCore/Configurations/Observability/LlmCallUsageConfiguration.cs
// `observability.llm_call_usage` tablo şeması — LLM çağrı maliyeti ve token kullanımı.
// called_at ve model üzerinde index: analytics sorgularında tarih/model filtrelemesi için.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Observability;

internal sealed class LlmCallUsageConfiguration : IEntityTypeConfiguration<LlmCallUsageEntity>
{
    public void Configure(EntityTypeBuilder<LlmCallUsageEntity> builder)
    {
        builder.ToTable("llm_call_usage", Schemas.Observability);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(e => e.Model)
            .HasColumnName("model")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.InputTokens)
            .HasColumnName("input_tokens")
            .IsRequired();

        builder.Property(e => e.OutputTokens)
            .HasColumnName("output_tokens")
            .IsRequired();

        builder.Property(e => e.CostUsd)
            .HasColumnName("cost_usd")
            .HasColumnType("numeric(12,8)")
            .IsRequired();

        builder.Property(e => e.DurationMs)
            .HasColumnName("duration_ms")
            .IsRequired();

        builder.Property(e => e.CalledAt)
            .HasColumnName("called_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(e => e.CalledAt)
            .HasDatabaseName("ix_llm_call_usage_called_at")
            .IsDescending();

        builder.HasIndex(e => e.Model)
            .HasDatabaseName("ix_llm_call_usage_model");
    }
}
