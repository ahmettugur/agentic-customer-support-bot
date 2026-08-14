// Core/Model/StockDeductionResult.cs
// Çok satırlı stok düşümünün sonucu.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Stoğu yetmeyen tek bir ürün.
/// </summary>
/// <param name="Product">Kanonik ürün adı.</param>
/// <param name="Requested">İstenen adet.</param>
/// <param name="Available">Düşüm denenirken rafta bulunan adet.</param>
public sealed record StockShortage(string Product, int Requested, int Available);

/// <summary>
/// Çok satırlı bir siparişin stok düşümü sonucu.
///
/// <para>
/// <b>Ya hep ya hiç.</b> Satırlardan biri bile yetmezse hiçbiri düşülmez — aksi hâlde
/// "3 üründen 2'si düşüldü, sipariş oluşmadı" gibi bir durumda stok sessizce kaybolurdu.
/// Bu garanti adapter tarafında tek bir veritabanı transaction'ı ile sağlanır.
/// </para>
/// </summary>
/// <param name="Success">Tüm satırlar düşüldüyse <c>true</c>.</param>
/// <param name="Shortages">
/// Başarısızlıkta yetersiz kalan satırlar — kullanıcıya "hangi üründen kaç adet var"
/// diyebilmek için. Başarıda boştur.
/// </param>
public sealed record StockDeductionResult(bool Success, IReadOnlyList<StockShortage> Shortages)
{
    public static StockDeductionResult Ok() => new(true, []);

    public static StockDeductionResult Insufficient(IReadOnlyList<StockShortage> shortages) =>
        new(false, shortages);
}
