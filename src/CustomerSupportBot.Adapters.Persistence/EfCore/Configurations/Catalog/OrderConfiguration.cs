using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Catalog;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable("orders", Schemas.Catalog);

        builder.HasKey(e => e.Code);

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .UseIdentityByDefaultColumn()
            .IsRequired();

        builder.Property(e => e.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.OrderDate)
            .HasColumnName("order_date")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.CancelReason)
            .HasColumnName("cancel_reason")
            .HasMaxLength(512);

        builder.Property(e => e.ReturnRequestedAt)
            .HasColumnName("return_requested_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.ReturnReason)
            .HasColumnName("return_reason")
            .HasMaxLength(512);

        builder.HasIndex(e => e.CustomerId)
            .HasDatabaseName("ix_orders_customer_id");
    }
}
