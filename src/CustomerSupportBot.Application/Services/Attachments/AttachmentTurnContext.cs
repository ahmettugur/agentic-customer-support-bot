// Application/Services/Attachments/AttachmentTurnContext.cs
// Mesaja eklenen fotoğrafları doğrular ve açıklamalarını turun metnine ekler.

using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Attachments;

/// <summary>
/// Ajanlar görüntüyü değil, görsel modelin ürettiği açıklamayı görür. Açıklama kullanıcının mesajına
/// eklenir ve geçmişe de bu hâliyle yazılır — böylece fotoğraf bir turda, sipariş numarası sonraki
/// turda gelse bile ajan fotoğrafı bilir; temsilci panelinde de görünür. Açıklama yüklemede
/// <c>IInputGuard</c>'dan geçmiştir; kullanıcı rolündeki metin olarak, kullanıcının yazdığıyla aynı
/// güven düzeyindedir.
/// </summary>
public static class AttachmentTurnContext
{
    /// <summary>
    /// İstemcinin gönderdiği kimliklerden yalnızca BU oturuma ve BU müşteriye ait olanları, ilk
    /// <paramref name="maxPerMessage"/> tanesiyle döner. Diğerleri sessizce yok sayılır.
    /// </summary>
    public static async Task<IReadOnlyList<ChatAttachment>> ResolveAsync(
        IAttachmentStore store,
        IReadOnlyList<string>? ids,
        string sessionId,
        string? customerId,
        int maxPerMessage,
        CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return [];

        var owned = (await store.ListForSessionAsync(sessionId, ct))
            .Where(a => string.Equals(a.CustomerId, customerId, StringComparison.Ordinal))
            .ToDictionary(a => a.Id);

        return ids
            .Distinct(StringComparer.Ordinal)
            .Where(owned.ContainsKey)
            .Take(maxPerMessage)
            .Select(id => owned[id])
            .ToList();
    }

    /// <summary>Mesaj + her fotoğraf için bir analiz satırı. Fotoğraf yoksa mesaj aynen döner.</summary>
    public static string Compose(string query, IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachments.Count == 0) return query;

        var sb = new StringBuilder(query.TrimEnd());
        sb.AppendLine();
        for (var i = 0; i < attachments.Count; i++)
        {
            var label = attachments.Count == 1 ? "fotoğraf" : $"fotoğraf {i + 1}";
            var description = attachments[i].Description;
            sb.AppendLine();
            sb.Append(string.IsNullOrWhiteSpace(description)
                ? $"[Müşterinin eklediği {label} — otomatik analiz yapılamadı; temsilci fotoğrafı görebilir]"
                : $"[Müşterinin eklediği {label} — otomatik analiz]: {description}");
        }
        return sb.ToString();
    }
}
