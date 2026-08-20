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
