// Models/FakeDatabase.cs
// ASP.NET Core çok iş parçacıklı olduğundan ConcurrentDictionary kullanılır.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Ürün bilgisini temsil eder. Fiyat sabit, stok sipariş verildiğinde azalır.
/// </summary>
public class ProductInfo
{
    public decimal Price { get; set; }
    public int Stock { get; set; }

    public ProductInfo(decimal price, int stock)
    {
        Price = price;
        Stock = stock;
    }
}