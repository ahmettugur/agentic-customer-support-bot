using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Ürün kataloğu için secondary port.
///</summary>
public interface IProductCatalogRepository
{
    /// <summary>Tam eşleşme veya fuzzy match ile ürün bulur. Bulunamazsa null döner.</summary>
    ProductInfo? FindProduct(string productName);

    /// <summary>
    /// Bir siparişin <b>tüm</b> satırlarının stoğunu tek seferde düşer.
    ///
    /// <para>
    /// Ya hep ya hiç: satırlardan biri bile yetmezse hiçbiri düşülmez ve
    /// <see cref="StockDeductionResult.Shortages"/> yetersiz kalan satırları taşır.
    /// Çağıran taraf tek tek düşüp elle telafi etmek zorunda kalmasın diye atomiklik
    /// adapter'ın sorumluluğundadır (Postgres tarafında tek transaction).
    /// </para>
    ///
    /// <para>
    /// <paramref name="lines"/> içindeki ürün adları <b>kanonik</b> olmalıdır
    /// (<see cref="FindProduct"/> ile çözülmüş) ve aynı ürün birden fazla satırda
    /// tekrarlanmamalıdır — tekrar, aynı satırın iki kez düşülmesi demektir.
    /// </para>
    /// </summary>
    /// <summary>
    /// Stoğu kendi başına, kendi transaction'ında düşer.
    ///
    /// <para>
    /// <b>Sipariş akışında KULLANILMAMALIDIR</b> — orada <see cref="IOrderRepository.PlaceOrder"/>
    /// vardır. Sipariş verirken bu metodu çağırmak, düşümü siparişin yazılmasından ayrı bir
    /// transaction'a koyar; ikisi arasında oluşan bir hata stoğu düşülmüş ama karşılığında hiç
    /// sipariş oluşmamış hâlde bırakır ve bu hiçbir yerde görünmez. Sipariş akışı tam olarak
    /// bu sebeple <c>PlaceOrder</c>'a taşındı.
    /// </para>
    ///
    /// <para>
    /// Şu an üretimde çağıranı yoktur; yalnızca kendi testleri kullanır.
    /// </para>
    /// </summary>
    StockDeductionResult TryDeductStock(IReadOnlyList<OrderLine> lines);

    /// <summary>Tüm ürün listesi.</summary>
    IReadOnlyList<ProductInfo> GetAll();

    /// <summary>
    /// Belirli kategorideki ürünleri döner. Kategorinin hiç bulunamamasıyla bulunup boş
    /// olmasını ayırt eder — bkz. <see cref="CategoryProducts"/>.
    /// </summary>
    CategoryProducts GetByCategory(string category);

    /// <summary>
    /// Kullanıcıya <b>seçenek olarak sunulabilecek</b> kategori adlarını sıralı döner:
    /// yalnızca en az bir ürünü olanlar.
    ///
    /// <para>
    /// Boş kategoriler kasıtlı olarak dışarıda bırakılır. Bu liste kategori seçim ekranını
    /// besler; içi boş bir kategoriyi seçenek olarak göstermek kullanıcıyı tıkladığında
    /// "ürün bulunmamaktadır" ile karşılaşacağı bir çıkmaz sokağa sokar.
    /// </para>
    /// </summary>
    IReadOnlyList<string> GetSelectableCategories();
}
