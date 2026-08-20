namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface ICustomerRepository
{
    bool Exists(long customerId);

    /// <summary>
    /// Verilen e-postanın <b>bu müşteriye</b> ait olup olmadığını söyler (büyük/küçük harf
    /// duyarsız). Müşteri yoksa veya kayıtlı e-postası yoksa <c>false</c>.
    ///
    /// <para>
    /// Public kayıt akışının kimlik sahipliği kontrolüdür. Bu olmadan
    /// <see cref="Exists"/> yalnızca "böyle bir müşteri var mı" sorusunu yanıtlıyordu ve
    /// herkes başkasının müşteri numarasıyla hesap açıp o müşteri adına geçerli bir JWT
    /// alabiliyordu — yani sistemin geri kalanındaki tüm sahiplik kontrolleri (EntityVerifier,
    /// oturum sahipliği, tool sahiplik kuralları) doğru müşteri sanıp geçiriyordu.
    /// </para>
    /// </summary>
    Task<bool> IsEmailOwnedByCustomerAsync(long customerId, string email, CancellationToken ct = default);

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
