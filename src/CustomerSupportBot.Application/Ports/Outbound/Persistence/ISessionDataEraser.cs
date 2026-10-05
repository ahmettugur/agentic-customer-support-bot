// Ports/Outbound/Persistence/ISessionDataEraser.cs
// Oturuma bağlı kişisel veriyi silen depolar — veri saklama süresi ve KVKK silme talebi ortak kullanır.

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Bir oturuma bağlı veri tutan her depo (sohbet köprüsü, oturum modu, puanlar, eskalasyonlar, akıl
/// yürütme izleri, episodik bellek) bunu uygular. Yeni bir depo silmeye katılmak için yalnızca bu
/// arayüzü uygular ve DI'a kaydedilir — silme servisini değiştirmek gerekmez.
///
/// <para>
/// Uygulamalar kalıcı kaydı ve <b>tüm pod'lardaki</b> önbelleği temizler. Silme tekrarlanabilir
/// olmalıdır: olmayan bir oturumu silmek hata değildir.
/// </para>
/// </summary>
public interface ISessionDataEraser
{
    /// <summary>Raporlarda ve loglarda görünen kısa ad (ör. "ratings").</summary>
    string Name { get; }

    /// <summary>Verilen oturumlara ait kayıtları siler; silinen kayıt sayısını döner.</summary>
    Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default);
}

/// <summary>
/// Oturumdan bağımsız, doğrudan müşteriye bağlı veri tutan depolar (ör. müşteri etiketli episodik
/// bellek). Yalnızca müşteri silme talebinde çağrılır.
/// </summary>
public interface ICustomerDataEraser
{
    string Name { get; }

    Task<int> EraseCustomerAsync(string customerId, CancellationToken ct = default);
}
