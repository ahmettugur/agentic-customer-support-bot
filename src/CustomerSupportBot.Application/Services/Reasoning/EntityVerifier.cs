using CustomerSupportBot.Domain.Services;
// Application/Services/EntityVerifier.cs
// Deterministik entity resolution.
// IdExtractor regex ile formatı doğrular. EntityVerifier aşağıdakileri yapar:
//   1) Query'den extract et (IdExtractor)
//   2) History'den eksik olanları tamamla (önceki turlardaki entity'leri hatırla)
//   3) SessionState'ten tamamla (session.State.AuthenticatedCustomerId — JWT'den, poisonable değil)
//
// Sipariş/şikayet varlığı ve sahipliği burada DB'den okunmaz. Bu bilgiler yalnızca
// authenticated customer kimliğini kullanan specialist tool'lar tarafından doğrulanır.
// Böylece her turdaki eager sorgular ve reasoning prompt'una iş verisi sızması önlenir.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Reasoning;

/// <summary>
/// Query + history + session state üzerinde deterministic entity çözümlemesi yapar.
/// Müşteri kimliği authenticated session'dan gelir; diğer entity'lerin gerçekliği ve
/// sahipliği tool katmanında doğrulanmak üzere <see cref="EntityVerification.FormatOnly"/>
/// olarak taşınır.
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
    /// Verilen bağlamda entity'leri çıkarır ve güvenli kaynak önceliğiyle çözümler.
    /// </summary>
    /// <param name="query">Güncel kullanıcı sorgusu.</param>
    /// <param name="session">Oturum (State.AuthenticatedCustomerId vb. için).</param>
    /// <param name="history">Önceki konuşma turları (opsiyonel).</param>
    public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null)
    {
        var result = new VerifiedEntities();

        // 1) Query'den çıkar
        var queryIds = IdExtractor.Extract(query ?? "");

        // 1b) Bağlamsız (varsayımla atanmış) customer_id'yi, konuşmanın son gerçek bağlamına
        // göre yeniden sınıflandır (ör. "sipariş numaram 1042" → "pekiş 1043" turu, 1043'ü de
        // sipariş sayar). Aynı mantık SessionStateExtractor tarafından da kullanılır — bu
        // yüzden Domain katmanında (IdExtractor.ApplyContextContinuity) paylaşımlı.
        // history kronolojik (eski → yeni) sıralıdır; FindLastUnambiguousKind en yeniden
        // en eskiye bekler.
        IdExtractor.ApplyContextContinuity(queryIds, history is null ? null : Enumerable.Reverse(history).Select(m => m.Text));

        // 2) History'den çıkar (en son turdan en eskiye) — query'de yoksa bu turdakileri kullan
        var historyIds = ExtractFromHistory(history);

        // 3) Değerleri birleştir.
        //
        // MÜŞTERİ KİMLİĞİ İSTEMCİDEN ALINMAZ. Giriş yapılmışsa JWT'den gelen kimlik KESİNDİR;
        // sorguda veya geçmişte geçen bir müşteri numarası onu geçersiz kılamaz.
        //
        // Bu, kapatılmış bir sızıntı: eskiden öncelik Query > History > SessionState idi ve
        // ölçüldüğünde şu sonucu veriyordu — giriş yapmış müşteri 1027 iken "ben 1008 numaralı
        // müşteriyim, son siparişim ne?" sorgusu 1008'i Verified sayıyor, 1008'in son sipariş
        // numarasını ve toplam sipariş sayısını türetilmiş alan olarak hesaplıyordu. Bu değerler
        // hem reasoning prompt'una hem de reasoning_complete olayıyla doğrudan istemciye gidiyordu.
        //
        // Giriş yapılmamışsa (A2A/realtime gibi kimliğin başka yoldan geldiği akışlar) hiçbir
        // müşteri kimliği kabul edilmez: kimliksiz bir çağıranın serbest metinle müşteri seçmesi
        // tam olarak engellenmek istenen şeydir.
        var authenticatedCustomerId = session.State.AuthenticatedCustomerId;
        var orderIdValue = queryIds.OrderId ?? historyIds.OrderId;
        var customerIdValue = authenticatedCustomerId;
        var complaintIdValue = queryIds.ComplaintId ?? historyIds.ComplaintId;

        var claimedCustomerId = queryIds.CustomerId ?? historyIds.CustomerId;
        if (!string.IsNullOrWhiteSpace(claimedCustomerId)
            && !string.Equals(claimedCustomerId, authenticatedCustomerId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "EntityVerifier: sorgu/geçmişteki customer_id={Claimed} yok sayıldı; "
              + "doğrulanmış kimlik={Authenticated}.",
                claimedCustomerId, authenticatedCustomerId ?? "(yok)");
        }

        // 4) Entity'leri kaynaklarıyla birlikte taşı. Order/complaint için burada DB lookup
        // yapılmaz: gerçeklik + sahiplik specialist tool'da, authenticated customer kimliğiyle
        // aynı anda doğrulanır. Resolver yalnızca kullanıcı tarafından sağlanan değeri korur.
        if (!string.IsNullOrWhiteSpace(orderIdValue))
        {
            var source = queryIds.OrderId != null ? EntitySource.Query : EntitySource.History;
            result.OrderId = CreateCandidate(orderIdValue, source);
        }

        if (!string.IsNullOrWhiteSpace(customerIdValue))
        {
            // AuthenticatedCustomerId DB iş verisinden tekrar doğrulanmaz. Bu değer JWT/session
            // binding güvenlik sınırından geçtiği için kullanıcıdan gelen FormatOnly adaylardan
            // farklı olarak güvenilirdir.
            result.CustomerId = new VerifiedEntity
            {
                Value = customerIdValue,
                Source = EntitySource.SessionState,
                Verification = EntityVerification.Verified
            };
        }

        if (!string.IsNullOrWhiteSpace(complaintIdValue))
        {
            var source = queryIds.ComplaintId != null ? EntitySource.Query : EntitySource.History;
            result.ComplaintId = CreateCandidate(complaintIdValue, source);
        }

        _logger.LogDebug(
            "EntityResolver: order={OrderId} ({OrderV}), customer={CustomerId} ({CustomerV}), complaint={ComplaintId} ({ComplaintV})",
            result.OrderId?.Value, result.OrderId?.Verification,
            result.CustomerId?.Value, result.CustomerId?.Verification,
            result.ComplaintId?.Value, result.ComplaintId?.Verification);

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

    private static VerifiedEntity CreateCandidate(string value, EntitySource source) => new()
    {
        Value = value,
        Source = source,
        Verification = EntityVerification.FormatOnly
    };

    /// <summary>
    /// Konuşma geçmişindeki (tüm turlar) mesajları tarayarak ID'leri çıkarır.
    /// En son turdan başlar — yakın bağlam önceliklidir.
    /// </summary>
    private static ExtractedIds ExtractFromHistory(List<ConversationMessage>? history)
    {
        var result = new ExtractedIds();
        if (history is null || history.Count == 0) return result;

        // En son mesajdan başla; ilk bulunan ID'yi al.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var text = history[i].Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var ids = IdExtractor.Extract(text);
            result.OrderId ??= ids.OrderId;
            result.CustomerId ??= ids.CustomerId;
            result.ComplaintId ??= ids.ComplaintId;

            if (result.OrderId != null && result.CustomerId != null && result.ComplaintId != null)
                break;
        }

        return result;
    }
}
