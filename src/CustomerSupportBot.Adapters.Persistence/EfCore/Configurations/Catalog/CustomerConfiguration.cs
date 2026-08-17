using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Catalog;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<CustomerEntity>
{
    public void Configure(EntityTypeBuilder<CustomerEntity> builder)
    {
        builder.ToTable("customers", Schemas.Catalog);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn()
            .IsRequired();

        builder.Property(e => e.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(256);

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(32);

        builder.HasIndex(e => e.FullName)
            .HasDatabaseName("ix_customers_full_name");
    }
}
