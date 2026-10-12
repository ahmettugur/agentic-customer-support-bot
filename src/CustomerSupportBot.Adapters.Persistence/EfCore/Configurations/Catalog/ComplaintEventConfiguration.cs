using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Catalog;

internal sealed class ComplaintEventConfiguration : IEntityTypeConfiguration<ComplaintEventEntity>
{
    public void Configure(EntityTypeBuilder<ComplaintEventEntity> builder)
    {
        builder.ToTable("complaint_events", Schemas.Catalog);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(e => e.ComplaintCode).HasColumnName("complaint_code").IsRequired();
        builder.Property(e => e.At).HasColumnName("at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.Actor).HasColumnName("actor").HasMaxLength(128).IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(e => e.FromStatus).HasColumnName("from_status").HasMaxLength(64);
        builder.Property(e => e.ToStatus).HasColumnName("to_status").HasMaxLength(64);
        builder.Property(e => e.AssignedAgentId).HasColumnName("assigned_agent_id").HasMaxLength(64);
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(2000);

        builder.HasOne<ComplaintEntity>()
            .WithMany()
            .HasForeignKey(e => e.ComplaintCode)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ComplaintCode, e.At })
            .HasDatabaseName("ix_complaint_events_code_at");
    }
}
