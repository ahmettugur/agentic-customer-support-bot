using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Müşteri profil güncelleme port'u — adapter'ların etkileşim kaydı yazması için.
/// </summary>
public interface ICustomerProfileService
{
    Task<CustomerProfile?> RecordInteractionAsync(
        string? customerId,
        string userQuery,
        string botResponse,
        string? intent,
        int? rating = null,
        bool isNewSession = false,
        CancellationToken ct = default);

    /// <summary>Müşterinin açıkça söylediği tercihi kaydeder. Kapalı sözlük dışındaki anahtar/değer reddedilir (<c>false</c>) ve profil oluşturulmaz.</summary>
    Task<bool> SetPreferenceAsync(string customerId, string key, string value, CancellationToken ct = default);

    /// <summary>Tüm kayıtlı tercihleri siler; profilin geri kalanına dokunmaz.</summary>
    Task ClearPreferencesAsync(string customerId, CancellationToken ct = default);
}
