// analytics.message_feedback — bkz. MessageFeedbackEntity.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Analytics;

internal sealed class MessageFeedbackConfiguration : IEntityTypeConfiguration<MessageFeedbackEntity>
{
    public void Configure(EntityTypeBuilder<MessageFeedbackEntity> builder)
    {
        builder.ToTable("message_feedback", Schemas.Analytics);
        builder.HasKey(f => new { f.SessionId, f.MessageHash });

        builder.Property(f => f.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(f => f.MessageHash).HasColumnName("message_hash").HasMaxLength(32).IsRequired();
        builder.Property(f => f.Helpful).HasColumnName("helpful").IsRequired();
        builder.Property(f => f.Reason).HasColumnName("reason").HasMaxLength(16);
        builder.Property(f => f.Comment).HasColumnName("comment");
        builder.Property(f => f.TraceId).HasColumnName("trace_id").HasMaxLength(64);
        builder.Property(f => f.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(f => f.UpdatedAt).HasDatabaseName("ix_message_feedback_updated_at");
        builder.HasIndex(f => f.TraceId).HasDatabaseName("ix_message_feedback_trace_id");
    }
}
