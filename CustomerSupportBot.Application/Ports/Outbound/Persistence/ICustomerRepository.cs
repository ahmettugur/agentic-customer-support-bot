namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface ICustomerRepository
{
    bool Exists(long customerId);

    Task<string?> GetFullNameAsync(long customerId, CancellationToken ct = default);
}
