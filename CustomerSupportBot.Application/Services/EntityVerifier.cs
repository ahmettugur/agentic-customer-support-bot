using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Services;
// Application/Services/EntityVerifier.cs
// ReAct-lite entity grounding.
// IdExtractor regex ile formatı doğrular. EntityVerifier aşağıdakileri yapar:
//   1) Query'den extract et (IdExtractor)
//   2) History'den eksik olanları tamamla (önceki turlardaki entity'leri hatırla)
//   3) SessionState'ten tamamla (session.State.CustomerId)
//   4) DB ile varlık doğrulaması yap (FakeDatabase.OrdersDb/ComplaintsDb lookup)
//   5) Türetilmiş alanları hesapla (ör. customer_id'den last_order_id)
//
// Çıktı VerifiedEntities olarak reasoning prompt'una enjekte edilir.
// Böylece reasoning modeli "zaten bilinen bilgi için clarification isteme" kararını
// Tahmin üzerinden değil, grounded doğrulama üzerinden verir.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Query + history + session state üzerinde deterministic entity çıkarımı yapar
/// Ve FakeDatabase ile doğrulayarak yapılandırılmış bir sonuç döner.
/// Hiçbir LLM çağrısı yapmaz — tamamen deterministik.
/// </summary>
public class EntityVerifier
{
    private readonly ILogger<EntityVerifier> _logger;
    private readonly IOrderRepository _orders;
    private readonly IComplaintRepository _complaints;

    public EntityVerifier(
        IOrderRepository orders,
        IComplaintRepository complaints,
        ILogger<EntityVerifier> logger)
    {
        _orders = orders;
        _complaints = complaints;
        _logger = logger;
    }

    /// <summary>
    /// Verilen bağlamda entity'leri çıkarır, doğrular ve türetilmiş alanları hesaplar.
    /// </summary>
    /// <param name="query">Güncel kullanıcı sorgusu.</param>
    /// <param name="session">Oturum (State.CustomerId vb. için).</param>
    /// <param name="history">Önceki konuşma turları (opsiyonel).</param>
    public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ChatMessage>? history = null)
    {
        var result = new VerifiedEntities();

        // 1) Query'den çıkar
        var queryIds = IdExtractor.Extract(query ?? "");

        // 2) History'den çıkar (en son turdan en eskiye) — query'de yoksa bu turdakileri kullan
        var historyIds = ExtractFromHistory(history);

        // 3) Değerleri birleştir — öncelik: Query > History > SessionState
        var orderIdValue = queryIds.OrderId ?? historyIds.OrderId;
        var customerIdValue = queryIds.CustomerId ?? historyIds.CustomerId ?? session.State.CustomerId;
        var complaintIdValue = queryIds.ComplaintId ?? historyIds.ComplaintId;

        // 4) Her entity için kaynak + doğrulama
        if (!string.IsNullOrWhiteSpace(orderIdValue))
        {
            var source = queryIds.OrderId != null ? EntitySource.Query : EntitySource.History;
            result.OrderId = VerifyOrder(orderIdValue, source);
        }

        if (!string.IsNullOrWhiteSpace(customerIdValue))
        {
            var source = queryIds.CustomerId != null
                ? EntitySource.Query
                : (historyIds.CustomerId != null ? EntitySource.History : EntitySource.SessionState);
            result.CustomerId = VerifyCustomer(customerIdValue, source);
        }

        if (!string.IsNullOrWhiteSpace(complaintIdValue))
        {
            var source = queryIds.ComplaintId != null ? EntitySource.Query : EntitySource.History;
            result.ComplaintId = VerifyComplaint(complaintIdValue, source);
        }

        // 5) Türetilmiş alanlar — sadece verified customer varsa
        if (result.CustomerId?.Verification == EntityVerification.Verified)
        {
            var lastOrder = _orders.GetLast(customerIdValue!);
            if (lastOrder.HasValue)
            {
                result.DerivedLastOrderId = lastOrder.Value.OrderId;
            }
            result.DerivedOrderCount = _orders.GetByCustomer(customerIdValue!).Count;
        }

        _logger.LogDebug(
            "EntityVerifier: order={OrderId} ({OrderV}), customer={CustomerId} ({CustomerV}), complaint={ComplaintId} ({ComplaintV})",
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
            "[VERIFIED ENTITIES — session/DB ile doğrulandı]",
            "Aşağıdaki bilgiler ZATEN elinizde. requiredInfo'ya EKLEMEYİN, kullanıcıdan tekrar İSTEMEYİN."
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

        // Türetilmiş alanlar
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

        // Doğrulanmamış entity'ler için özel uyarı
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

    private VerifiedEntity VerifyOrder(string orderId, EntitySource source)
    {
        var entity = new VerifiedEntity { Value = orderId, Source = source };

        var order = _orders.Get(orderId);
        if (order != null)
        {
            entity.Verification = EntityVerification.Verified;
            entity.Attributes = new Dictionary<string, string>
            {
                ["status"] = order.Status,
                ["product"] = order.Product,
                ["quantity"] = order.Quantity.ToString(),
                ["customerId"] = order.CustomerId
            };
        }
        else
        {
            entity.Verification = EntityVerification.NotFoundInDb;
        }

        return entity;
    }

    private VerifiedEntity VerifyCustomer(string customerId, EntitySource source)
    {
        var entity = new VerifiedEntity { Value = customerId, Source = source };

        // Customer için explicit DB yok — en az bir sipariş veya şikayet varsa verified sayarız.
        var hasOrder = _orders.GetByCustomer(customerId).Count > 0;
        var hasComplaint = _complaints.GetByCustomer(customerId).Count > 0;

        if (hasOrder || hasComplaint)
        {
            entity.Verification = EntityVerification.Verified;
            entity.Attributes = new Dictionary<string, string>
            {
                ["has_orders"] = hasOrder ? "true" : "false",
                ["has_complaints"] = hasComplaint ? "true" : "false"
            };
        }
        else
        {
            // Customer için format doğruluğu yeterli — tool seviyesinde yine başarısız olabilir
            // Ama reasoning aşamasında "tamamen yanlış ID" demeyi tercih etmiyoruz.
            entity.Verification = EntityVerification.FormatOnly;
        }

        return entity;
    }

    private VerifiedEntity VerifyComplaint(string complaintId, EntitySource source)
    {
        var entity = new VerifiedEntity { Value = complaintId, Source = source };

        var complaint = _complaints.Get(complaintId);
        if (complaint != null)
        {
            entity.Verification = EntityVerification.Verified;
            entity.Attributes = new Dictionary<string, string>
            {
                ["status"] = complaint.Status,
                ["orderId"] = complaint.OrderId,
                ["customerId"] = complaint.CustomerId
            };
        }
        else
        {
            entity.Verification = EntityVerification.NotFoundInDb;
        }

        return entity;
    }

    /// <summary>
    /// Konuşma geçmişindeki (tüm turlar) mesajları tarayarak ID'leri çıkarır.
    /// En son turdan başlar — yakın bağlam önceliklidir.
    /// </summary>
    private static ExtractedIds ExtractFromHistory(List<ChatMessage>? history)
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
