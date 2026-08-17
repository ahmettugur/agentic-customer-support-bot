// Model/CategoryProducts.cs
// Kategori bazlı ürün sorgusunun sonucu.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir kategori sorgusunun sonucu.
///
/// <para>
/// Boş bir ürün listesi <b>tek başına</b> yeterli bilgi değildir: "böyle bir kategori yok"
/// ile "kategori var ama içi boş" çağıran için taban tabana zıt iki durumdur. Birincisinde
/// ad yanlıştır ve düzeltilip tekrar denenmelidir; ikincisinde tekrar denemek anlamsızdır.
/// Eskiden ikisi de <c>[]</c> dönüyor ve aynı hata mesajına iniyordu — LLM hangi durumda
/// olduğunu bilemediği için kendini düzeltemiyordu.
/// </para>
/// </summary>
/// <param name="CategoryExists">Katalogda bu adda bir kategori bulundu mu.</param>
/// <param name="CanonicalName">
/// Katalogdaki kanonik yazım (ör. çağıran "içecekler" yazdıysa "İçecekler"). Kategori
/// bulunamadıysa <c>null</c>. Kolon case/aksan duyarsız collation'lı olduğu için eşleşme
/// farklı yazımlarla da olur; kullanıcıya ve LLM'e dönen metin kanonik yazımı taşımalıdır.
/// </param>
/// <param name="Products">Kategorideki ürünler; kategori yoksa veya boşsa boş liste.</param>
public sealed record CategoryProducts(
    bool CategoryExists,
    string? CanonicalName,
    IReadOnlyList<ProductInfo> Products)
{
    /// <summary>Katalogda böyle bir kategori yok.</summary>
    public static CategoryProducts NotFound() => new(false, null, []);

    /// <summary>Kategori var; <paramref name="products"/> boş olabilir.</summary>
    public static CategoryProducts Found(string canonicalName, IReadOnlyList<ProductInfo> products) =>
        new(true, canonicalName, products);
}
