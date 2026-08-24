// Application/Services/EntityVerifier.cs
// Deterministik entity resolution.
// EntityVerifier tek bir şey yapar: session.State.AuthenticatedCustomerId'yi (JWT'den,
// poisonable değil) VerifiedEntities.CustomerId olarak taşır.
//
// Sipariş/şikayet ID'si artık burada metinden çıkarılmaz (bkz. IdExtractor'ın kaldırılması —
// order_id/complaint_id çözümü artık tamamen LLM'e bırakıldı: specialist agent'lar kullanıcı
// mesajını doğrudan okuyup order_id'yi tool parametresi olarak geçiriyor; order_id yoksa
// get_last_order_tool otomatik son siparişi getiriyor, bkz. planning-agent.md). Sipariş/şikayet
// varlığı ve sahipliği zaten burada DB'den okunmuyordu — bu davranış değişmedi, yalnızca
// ID'yi tahmin eden regex katmanı kalktı.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Reasoning;

/// <summary>
/// Session state üzerinden deterministic müşteri kimliği çözümlemesi yapar.
/// Hiçbir LLM çağrısı yapmaz — tamamen deterministik.
/// </summary>
public class EntityVerifier
{
    private readonly ILogger<EntityVerifier> _logger;

    public EntityVerifier(ILogger<EntityVerifier> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Authenticated session'dan müşteri kimliğini çözümler.
    /// </summary>
    /// <param name="query">Güncel kullanıcı sorgusu (imza geriye dönük uyumluluk için korunur; kullanılmaz).</param>
    /// <param name="session">Oturum (State.AuthenticatedCustomerId için).</param>
    /// <param name="history">Önceki konuşma turları (imza geriye dönük uyumluluk için korunur; kullanılmaz).</param>
    public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null)
    {
        var result = new VerifiedEntities();

        // MÜŞTERİ KİMLİĞİ İSTEMCİDEN ALINMAZ. Giriş yapılmışsa JWT'den gelen kimlik KESİNDİR.
        // Giriş yapılmamışsa (A2A/realtime gibi kimliğin başka yoldan geldiği akışlar) hiçbir
        // müşteri kimliği kabul edilmez.
        var authenticatedCustomerId = session.State.AuthenticatedCustomerId;

        if (!string.IsNullOrWhiteSpace(authenticatedCustomerId))
        {
            // AuthenticatedCustomerId DB iş verisinden tekrar doğrulanmaz. Bu değer JWT/session
            // binding güvenlik sınırından geçtiği için güvenilirdir.
            result.CustomerId = new VerifiedEntity
            {
                Value = authenticatedCustomerId,
                Source = EntitySource.SessionState,
                Verification = EntityVerification.Verified
            };
        }

        _logger.LogDebug(
            "EntityResolver: customer={CustomerId} ({CustomerV})",
            result.CustomerId?.Value, result.CustomerId?.Verification);

        return result;
    }

    /// <summary>
    /// Prompt'a enjekte edilecek insan okunabilir özet satırlarını üretir.
    /// Hiç entity yoksa null döner.
    /// </summary>
    public static string? BuildPromptBlock(VerifiedEntities verified)
    {
        if (!verified.HasAny) return null;

        var lines = new List<string>
        {
            "[RESOLVED ENTITIES — query/history/authenticated session üzerinden çözümlendi]",
            "Aşağıdaki kimlik değerleri ZATEN sağlandı. requiredInfo'ya EKLEMEYİN ve kullanıcıdan tekrar İSTEMEYİN.",
            "FORMAT_ONLY değerlerin varlığını, sahipliğini veya özelliklerini varsaymayın; ilgili specialist tool ile doğrulayın."
        };

        if (verified.OrderId != null)
        {
            lines.Add(FormatEntityLine("order_id", verified.OrderId));
        }
        if (verified.CustomerId != null)
        {
            lines.Add(FormatEntityLine("customer_id", verified.CustomerId));
        }
        if (verified.ComplaintId != null)
        {
            lines.Add(FormatEntityLine("complaint_id", verified.ComplaintId));
        }

        // Geriye dönük sözleşme: başka bir çağıran türetilmiş alan sağlarsa render edilir.
        // EntityVerifier artık bu alanlar için DB sorgusu yapmaz ve onları kendisi üretmez.
        if (!string.IsNullOrEmpty(verified.DerivedLastOrderId))
        {
            lines.Add($"- last_order_id = \"{verified.DerivedLastOrderId}\" " +
                      $"[derived: customer_id'nin en son siparişi]");
        }
        if (verified.DerivedOrderCount.HasValue)
        {
            lines.Add($"- customer_order_count = {verified.DerivedOrderCount.Value} " +
                      $"[derived: müşterinin toplam siparişi]");
        }

        // Geriye dönük sözleşme: başka bir doğrulayıcı NotFoundInDb sağlarsa uyarı korunur.
        var notFoundEntities = new List<string>();
        if (verified.OrderId?.Verification == EntityVerification.NotFoundInDb)
            notFoundEntities.Add($"order_id={verified.OrderId.Value}");
        if (verified.ComplaintId?.Verification == EntityVerification.NotFoundInDb)
            notFoundEntities.Add($"complaint_id={verified.ComplaintId.Value}");

        if (notFoundEntities.Count > 0)
        {
            lines.Add("");
            lines.Add($"⚠️ UYARI: Şu entity'ler DB'de bulunamadı: {string.Join(", ", notFoundEntities)}. " +
                      "Kullanıcıya kibarca doğrulatın — yanlış numara vermiş olabilir.");
        }

        return string.Join('\n', lines);
    }

    // ─── İç yardımcılar ───

    private static string FormatEntityLine(string fieldName, VerifiedEntity entity)
    {
        var verifStr = entity.Verification switch
        {
            EntityVerification.Verified => "VERIFIED",
            EntityVerification.NotFoundInDb => "NOT_FOUND_IN_DB",
            EntityVerification.FormatOnly => "FORMAT_ONLY",
            _ => "UNKNOWN"
        };

        var line = $"- {fieldName} = \"{entity.Value}\" [{verifStr}, source={entity.Source}";

        if (entity.Attributes is { Count: > 0 })
        {
            var attrs = string.Join(", ", entity.Attributes.Select(kv => $"{kv.Key}={kv.Value}"));
            line += $", {attrs}";
        }

        line += "]";
        return line;
    }

}
