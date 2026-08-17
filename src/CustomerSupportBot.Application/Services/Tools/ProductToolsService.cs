using System.ComponentModel;
using System.Globalization;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Tools;

/// <summary>
/// Ürün sorgulama araçları uygulama servisi.
/// </summary>
public sealed class ProductToolsService : IProductToolsService
{
    private readonly IProductCatalogRepository _products;
    private readonly IUiHintEmitter _uiHint;

    public ProductToolsService(IProductCatalogRepository products, IUiHintEmitter uiHint)
    {
        _products = products;
        _uiHint = uiHint;
    }

    /// <summary>
    /// Fiyatı kullanıcıya gösterilecek biçimde yazar: <c>18,00 TL</c>.
    ///
    /// <para>
    /// Kültür <b>açıkça</b> tr-TR verilir, ortamın <c>CurrentCulture</c>'ına bırakılmaz —
    /// sunucu kültürü ortama göre değişir (container'larda genelde invariant) ve aynı fiyat
    /// bir yerde <c>18,00</c>, başka yerde <c>18.00</c> olarak çıkardı.
    /// </para>
    ///
    /// <para>
    /// Sembol yerine "TL" yazılır: bu metin hem LLM'e hem de sesli kanalda TTS'e gidiyor,
    /// "TL" her ikisinde de tek anlama gelir.
    /// </para>
    /// </summary>
    private static string FormatPrice(decimal price) =>
        $"{price.ToString("N2", TurkishCulture)} TL";

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    [Description("Ürün kataloğundan ürün bilgisi sorgular. Ürün adı veya kısmi adı ile arama yapar. " +
                 "Sonuç yapılandırılmış ToolResult olarak döner (success, confidence, data, error).")]
    public ToolResult ProductInquiryTool(
        [Description("Sorgulanacak ürünün adı veya kısmi adı")] string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return ToolResult.ValidationError(
                "Ürün adı boş olamaz. Hangi ürünü aradığınızı belirtin.",
                WellKnown.ToolParameterNames.ProductName);

        var product = _products.FindProduct(productName);
        if (product is null)
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");

        var isExact = product.Name.Equals(productName, StringComparison.OrdinalIgnoreCase);
        return ToolResult.Ok(
            message: $"{product.Name}: Fiyat = {FormatPrice(product.Price)}, Stok = {product.Stock} adet.",
            data: new { name = product.Name, price = product.Price, stock = product.Stock },
            confidence: isExact ? 1.0 : 0.85);
    }

    [Description("Ürün kataloğunu listeler. Kategori belirtilirse yalnızca o kategoriye ait ürünleri döndürür. " +
                 "Kategori belirtilmezse kullanıcıya kategori seçim ekranı gösterilir; " +
                 "bu durumda kullanıcının bir kategori seçmesini bekle ve seçilen kategoriyle tekrar çağır.")]
    public ToolResult ProductListTool(
        [Description("Filtrelenecek kategori adı (opsiyonel). Boş bırakılırsa kategori seçim ekranı gösterilir.")] string? category = null)
    {
        if (string.IsNullOrWhiteSpace(category))
            return ShowCategoryPicker();

        var result = _products.GetByCategory(category);

        // Kategori yok → ad yanlış; LLM geçerli adlarla tekrar deneyebilsin diye listeyi veriyoruz.
        if (!result.CategoryExists)
        {
            var selectable = _products.GetSelectableCategories();
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.CategoryNotFound,
                selectable.Count == 0
                    ? $"'{category}' diye bir kategori yok ve katalogda listelenebilir kategori bulunmuyor."
                    : $"'{category}' diye bir kategori yok. Geçerli kategoriler: {string.Join(", ", selectable)}. " +
                      "Bunlardan biriyle tekrar çağır.");
        }

        // Kategori var ama boş → aynı adla tekrar denemek anlamsız; bunu açıkça söylüyoruz.
        if (result.Products.Count == 0)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.ProductNotFound,
                $"'{result.CanonicalName}' kategorisi şu an boş — bu kategoride hiç ürün yok. " +
                "Aynı kategoriyle tekrar deneme; kullanıcıya başka bir kategori öner.");

        var products = result.Products;
        var lines = products.Select((p, i) =>
            $"{i + 1}. [{p.Category}] {p.Name} — {FormatPrice(p.Price)}, Stok: {p.Stock} adet");

        return ToolResult.Ok(
            message: string.Join("\n", lines),
            data: new
            {
                totalCount = products.Count,
                // Çağıranın yazdığı değil katalogdaki kanonik ad — aynı payload'daki
                // products[].category ile tutarlı olsun diye.
                category = result.CanonicalName,
                products = products.Select(p => new
                {
                    name     = p.Name,
                    category = p.Category,
                    price    = p.Price,
                    stock    = p.Stock
                }).ToList()
            });
    }

    /// <summary>
    /// Kategori seçim ekranını göstermeyi dener ve <b>gerçekte ne olduğunu</b> raporlar.
    ///
    /// <para>
    /// Buradaki kritik nokta: ipucu her zaman ekrana ulaşmaz. Sesli (native realtime) kanalda
    /// ambient session bağlamı kurulmadığı için <see cref="IUiHintEmitter.Emit"/> ipucunu
    /// düşürür ve kullanıcının bakacağı bir ekran yoktur. Eskiden bu durumda da LLM'e
    /// "kategori seçim ekranı gösterildi" deniyordu; model de kullanıcıyı olmayan bir ekrana
    /// yönlendiriyordu. Aynı şey katalogda listelenebilir kategori olmadığında da oluyordu:
    /// arayüz boş listeyi hiç çizmiyor, model ise seçim bekliyordu.
    /// </para>
    /// </summary>
    private ToolResult ShowCategoryPicker()
    {
        var categories = _products.GetSelectableCategories();

        if (categories.Count == 0)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.ProductNotFound,
                "Katalogda listelenebilir ürün bulunmuyor. Kullanıcıya kategori seçtirme; " +
                "şu an ürün listeleyemediğini bildir.");

        var shown = _uiHint.Emit(new StreamEvent(StreamEventTypes.UiHint,
            new { kind = "category_picker", categories }));

        // Ekran gerçekten çizildiyse kullanıcı tıklayacak; çizilmediyse (sesli kanal)
        // seçenekleri modelin sesli olarak okuması gerekir.
        return ToolResult.Ok(
            message: shown
                ? "Kategori seçim ekranı kullanıcıya gösterildi. Kullanıcı bir kategori seçtiğinde " +
                  "product_list_tool'u seçilen kategoriyle tekrar çağır."
                : "Bu kanalda görsel seçim ekranı gösterilemiyor. Kategorileri kullanıcıya kendin " +
                  $"ilet ve seçmesini iste: {string.Join(", ", categories)}. Seçim yapıldığında " +
                  "product_list_tool'u seçilen kategoriyle tekrar çağır.",
            data: new { kind = "category_picker", categories, pickerShown = shown });
    }
}
