using System.ComponentModel;
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
            message: $"{product.Name}: Fiyat = ${product.Price}, Stok = {product.Stock} adet.",
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
        {
            var categories = _products.GetCategories();
            _uiHint.Emit(new StreamEvent(StreamEventTypes.UiHint,
                new { kind = "category_picker", categories }));
            return ToolResult.Ok(
                message: "Kategori seçim ekranı kullanıcıya gösterildi. Kullanıcı bir kategori seçtiğinde " +
                         "product_list_tool'u seçilen kategoriyle tekrar çağır.",
                data: new { kind = "category_picker", categories });
        }

        var products = _products.GetByCategory(category);

        if (products.Count == 0)
            return ToolResult.NotFound(WellKnown.ToolErrorCodes.ProductNotFound,
                $"'{category}' kategorisinde ürün bulunmamaktadır.");

        var lines = products.Select((p, i) =>
            $"{i + 1}. [{p.Category}] {p.Name} — ${p.Price}, Stok: {p.Stock} adet");

        return ToolResult.Ok(
            message: string.Join("\n", lines),
            data: new
            {
                totalCount = products.Count,
                category,
                products = products.Select(p => new
                {
                    name     = p.Name,
                    category = p.Category,
                    price    = p.Price,
                    stock    = p.Stock
                }).ToList()
            });
    }
}
