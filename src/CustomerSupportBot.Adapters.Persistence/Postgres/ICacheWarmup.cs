// Postgres/ICacheWarmup.cs
// Hibrit cache adaptörlerinin açılışta asenkron ısıtılması.

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// Cache'ini DB'den dolduran (hydrate) hibrit adaptör.
///
/// <para>
/// Bu adaptörlerin okuma uçları senkron port arayüzlerinin arkasındadır ve cache boşsa ilk
/// çağrıda hydrate'i <c>GetAwaiter().GetResult()</c> ile bekler — yani ilk isteğin thread'i
/// DB okuması boyunca bloklanır. <c>PersistenceHydrator</c> açılışta
/// <see cref="WarmUpAsync"/>'i çağırarak cache'i istek gelmeden, asenkron doldurur; senkron yol
/// yalnızca ısıtma başarısız olursa devreye giren bir yedek olarak kalır.
/// </para>
/// </summary>
public interface ICacheWarmup
{
    Task WarmUpAsync(CancellationToken ct = default);
}
