// Ports/Driving/IPersonalizationPort.cs
// PRIMARY PORT — Müşteri profili yönetimi.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Müşteri profili CRUD ve konsolidasyon için primary (driving) port.
/// </summary>
public interface IPersonalizationPort
{
    (int Count, IReadOnlyList<CustomerProfile> Items) GetProfiles(int take = 100);
    CustomerProfile? GetProfile(string customerId);
    Task<CustomerProfile?> RefreshProfileAsync(string customerId, CancellationToken ct = default);
    CustomerProfile SetAdminNote(string customerId, string? note);
    bool DeleteProfile(string customerId);
}
