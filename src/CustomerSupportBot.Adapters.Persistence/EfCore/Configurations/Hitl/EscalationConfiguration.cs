// Infrastructure/Persistence/Configurations/Hitl/EscalationConfiguration.cs
// hitl.escalations tablosu — needs_escalation status'tan türeyen eskalasyonlar.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Hitl;

internal sealed class EscalationConfiguration : IEntityTypeConfiguration<EscalationEntity>
{
    /// <summary>Açık eskalasyon dedup kısıtının adı — ihlal yakalanırken bu adla eşleştirilir.</summary>
    public const string OpenDedupIndexName = "ux_escalations_open_session_agent";

    public void Configure(EntityTypeBuilder<EscalationEntity> builder)
    {
        builder.ToTable("escalations", Schemas.Hitl);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(64);

        builder.Property(e => e.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64);

        builder.Property(e => e.AgentName)
            .HasColumnName("agent_name")
            .HasMaxLength(64);

        builder.Property(e => e.UserQuery)
            .HasColumnName("user_query")
            .IsRequired();

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.Property(e => e.MissingContextJson)
            .HasColumnName("missing_context")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ResponseSummary)
            .HasColumnName("response_summary");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.AcknowledgedAt)
            .HasColumnName("acknowledged_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.AssignedTo)
            .HasColumnName("assigned_to")
            .HasMaxLength(128);

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution");

        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_escalations_status_created_at");

        // Session + ajan başına EN FAZLA BİR açık eskalasyon — dedup'ın asıl garantisi.
        //
        // EscalationPolicyService önce cache'e bakıp sonra INSERT ediyordu; ikisi atomik
        // değildi. Bileşik sorgunun paralel alt görevleri (ya da farklı pod'lar) aynı anda
        // kontrolden geçip aynı ajan için iki eskalasyon açabiliyordu. Kısıt yarışın kazananını
        // DB'de belirler; kaybeden mevcut kaydı alır (bkz. PostgresEscalationSink.CreateAsync).
        // Aynı index açık eskalasyonları session'a göre bulmak için de kullanılır.
        builder.HasIndex(e => new { e.SessionId, e.AgentName })
            .HasDatabaseName(OpenDedupIndexName)
            .IsUnique()
            .HasFilter("status IN ('Open', 'Acknowledged')");
    }
}
