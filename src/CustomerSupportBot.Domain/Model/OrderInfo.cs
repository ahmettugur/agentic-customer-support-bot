// Core/Model/OrderInfo.cs
// Domain modeli — sipariş bilgisi.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir siparişin tek satırı: hangi üründen kaç adet.
/// </summary>
/// <param name="Product">
/// Ürünün <b>kanonik</b> adı — katalogdan (<c>IProductCatalogRepository.FindProduct</c>)
/// çözülmüş hâli. Kullanıcının yazdığı ham metin ("kahve", "KAHVE") değil.
/// </param>
/// <param name="Quantity">Adet; her zaman 1 veya daha büyük.</param>
public sealed record OrderLine(string Product, int Quantity);

/// <summary>
/// Sipariş domain modeli.
///
/// <para>
/// Bir sipariş <b>birden fazla ürün satırı</b> taşıyabilir (<see cref="Lines"/>). Veritabanı
/// şeması bunu en baştan beri destekliyordu (<c>catalog.order_details</c> tablosu, sipariş
/// başına N kayıt); eskiden domain modeli yalnızca ilk satırı okuduğu için çok ürünlü sipariş
/// pratikte imkânsızdı. Artık tek gerçek kaynak bu listedir.
/// </para>
/// </summary>
public class OrderInfo
{
    /// <summary>
    /// Sipariş satırları. Geçerli bir siparişte en az bir eleman bulunur; aynı ürün iki kez
    /// yer almaz (<c>OrderPlacementTool</c> tekrar eden ürünleri tek satırda toplar — bu aynı
    /// zamanda <c>order_details</c> tablosunun (order_code, product_id) bileşik birincil
    /// anahtarının gereğidir).
    /// </summary>
    public List<OrderLine> Lines { get; set; } = [];

    public string CustomerId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; } = DateTime.Now;

    // ─── İptal bilgileri ───
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    // ─── İade bilgileri ───
    public DateTime? ReturnRequestedAt { get; set; }
    public string? ReturnReason { get; set; }

    /// <summary>
    /// Satırların insan-okunur tek satırlık özeti: <c>"Kahve x2, Çay x1"</c>.
    /// Tool mesajlarında, müşteri bağlamında ve entity attribute'larında kullanılır —
    /// böylece "hangi ürün" bilgisi çok satırlı siparişlerde de sessizce kaybolmaz.
    /// </summary>
    public string LinesSummary() =>
        Lines.Count == 0
            ? "—"
            : string.Join(", ", Lines.Select(l => $"{l.Product} x{l.Quantity}"));

    /// <summary>Siparişteki toplam ürün adedi (satırların adet toplamı).</summary>
    public int TotalQuantity() => Lines.Sum(l => l.Quantity);
}
