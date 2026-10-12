using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Catalog;

internal sealed class ComplaintConfiguration : IEntityTypeConfiguration<ComplaintEntity>
{
    public void Configure(EntityTypeBuilder<ComplaintEntity> builder)
    {
        builder.ToTable("complaints", Schemas.Catalog);

        builder.HasKey(e => e.Code);

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(e => e.Complaint)
            .HasColumnName("complaint")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(e => e.AssignedAgentId)
            .HasColumnName("assigned_agent_id")
            .HasMaxLength(64);

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution")
            .HasMaxLength(2000);

        builder.Property(e => e.ApprovalId)
            .HasColumnName("approval_id")
            .HasMaxLength(64);

        builder.Property(e => e.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_complaints_status_created_at");

        builder.HasIndex(e => e.OrderId)
            .HasDatabaseName("ix_complaints_order_id");

        builder.HasIndex(e => e.CustomerId)
            .HasDatabaseName("ix_complaints_customer_id");
    }
}
