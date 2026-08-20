// Sipariş verme ATOMİK Mİ?
//
// Sipariş vermek iki yazma içerir: ürün stoğunun düşülmesi ve siparişin yazılması. Bunlar ayrı
// transaction'larda yapıldığında aradaki herhangi bir hata — DB kesintisi, retry tükenmesi,
// pod'un ölmesi, geçersiz bir alanın yazma anında patlaması — stoğu düşülmüş ama karşılığında
// hiçbir sipariş oluşmamış hâlde bırakır.
//
// Bu arızanın kötülüğü sessiz olmasında: kullanıcı hata alır ve tekrar dener, ama stok bir
// daha geri gelmez. Yeterince tekrarlandığında ürün, aslında deposunda dururken "stokta yok"
// hâline gelir ve bunu hiçbir log göstermez.
//
// Test gerçek Postgres'e karşı koşar; transaction sınırları EF InMemory'de var olmadığı için
// bu hata orada aranamaz.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresOrderPlacementAtomicityTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresOrderPlacementAtomicityTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>Testler birbirinin stoğunu etkilemesin diye her test kendi ürününü yaratır.</summary>
    private async Task<(string Name, int Stock)> NewProductAsync(int stock)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var categoryId = await ctx.Categories.Select(c => c.Id).FirstAsync(TestContext.Current.CancellationToken);
        var name = $"Atomiklik Testi {Guid.NewGuid():N}";
        ctx.Products.Add(new ProductEntity
        {
            Name = name, Stock = stock, Price = 10m, CategoryId = categoryId
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (name, stock);
    }

    private async Task<int> StockOfAsync(string productName)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        return await ctx.Products.Where(p => p.Name == productName)
            .Select(p => p.Stock).FirstAsync(TestContext.Current.CancellationToken);
    }

    private static OrderInfo Order(string product, int qty, string customerId) => new()
    {
        Lines = [new OrderLine(product, qty)],
        CustomerId = customerId,
        Status = WellKnown.OrderStatuses.Processing,
        OrderDate = DateTime.UtcNow
    };

    /// <summary>
    /// ASIL BULGU. Stok düşüldükten SONRA, sipariş yazılırken patlayan bir hata seçiliyor
    /// (geçersiz müşteri kimliği, yazma anında ayrıştırılıyor). Stok geri gelmiş olmalı.
    /// </summary>
    [Fact]
    public async Task WhenWritingTheOrderFails_TheStockIsNotConsumed()
    {
        var (product, initial) = await NewProductAsync(50);

        var act = () => _fixture.OrderRepo.PlaceOrder(Order(product, 10, "gecersiz-musteri"));

        act.Should().Throw<Exception>("bu senaryonun amacı yazmanın başarısız olması");
        (await StockOfAsync(product)).Should().Be(initial,
            "sipariş oluşmadıysa stok da düşülmüş olmamalı");
    }

    /// <summary>Olağan akış bozulmamalı: başarılı sipariş hem stoğu düşürür hem kaydı yazar.</summary>
    [Fact]
    public async Task SuccessfulPlacement_DeductsStockAndWritesTheOrder()
    {
        var (product, initial) = await NewProductAsync(50);

        var result = _fixture.OrderRepo.PlaceOrder(Order(product, 10, "1001"));

        result.OrderId.Should().NotBeNullOrEmpty();
        (await StockOfAsync(product)).Should().Be(initial - 10);
        _fixture.OrderRepo.Get(result.OrderId!).Should().NotBeNull();
    }

    /// <summary>
    /// Stok yetersizse ne stok düşer ne sipariş yazılır — ve bu bir istisna değil,
    /// raporlanabilir bir sonuçtur.
    /// </summary>
    [Fact]
    public async Task InsufficientStock_LeavesNothingBehind()
    {
        var (product, initial) = await NewProductAsync(3);

        var result = _fixture.OrderRepo.PlaceOrder(Order(product, 10, "1001"));

        result.OrderId.Should().BeNull();
        result.Stock.Success.Should().BeFalse();
        result.Stock.Shortages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Product = product, Requested = 10, Available = 3 });
        (await StockOfAsync(product)).Should().Be(initial);
    }

    /// <summary>
    /// Çok satırlı siparişte SON satır yetersizse, ondan önce düşülmüş satırlar da geri
    /// alınmalı — yarım rezerve edilmiş bir sepet kalmamalı.
    /// </summary>
    [Fact]
    public async Task WhenOneLineIsShort_TheOtherLinesAreNotConsumed()
    {
        var (bol, bolInitial) = await NewProductAsync(100);
        var (az, azInitial) = await NewProductAsync(1);

        var order = new OrderInfo
        {
            Lines = [new OrderLine(bol, 5), new OrderLine(az, 10)],
            CustomerId = "1001",
            Status = WellKnown.OrderStatuses.Processing,
            OrderDate = DateTime.UtcNow
        };

        _fixture.OrderRepo.PlaceOrder(order).OrderId.Should().BeNull();

        (await StockOfAsync(bol)).Should().Be(bolInitial, "yetersiz satır tüm siparişi iptal eder");
        (await StockOfAsync(az)).Should().Be(azInitial);
    }
}
