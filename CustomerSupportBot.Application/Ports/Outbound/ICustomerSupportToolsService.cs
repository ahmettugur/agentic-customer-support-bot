namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Müşteri destek AI araçlarının tamamını kapsayan facade port.
/// Adapter'lar (CustomerSupportTeam, ApprovalGateService) bu arayüz üzerinden
/// tüm tool fonksiyonlarına erişir.
/// </summary>
public interface ICustomerSupportToolsService
    : IProductToolsService, IOrderToolsService, IComplaintToolsService
{
}
