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
}
