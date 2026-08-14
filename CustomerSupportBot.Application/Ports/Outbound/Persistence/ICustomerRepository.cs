namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface ICustomerRepository
{
    bool Exists(long customerId);

    Task<string?> GetFullNameAsync(long customerId, CancellationToken ct = default);

    /// <summary>
    /// Birden çok müşterinin adını tek sorguda getirir; bulunamayan kimlikler sonuçta yer almaz.
    ///
    /// <para>
    /// Tekil <see cref="GetFullNameAsync"/> yerine bunun var olma sebebi N+1'dir: admin onay
    /// kuyruğu bir listedir ve her kart için ayrı sorgu atmak, kuyruk büyüdükçe panelin
    /// açılışını doğrusal olarak yavaşlatırdı.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> GetFullNamesAsync(
        IReadOnlyCollection<long> customerIds, CancellationToken ct = default);
}
