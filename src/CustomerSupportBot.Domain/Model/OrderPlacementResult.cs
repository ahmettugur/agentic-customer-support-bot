// Core/Model/OrderPlacementResult.cs
// Stok düşümü ve sipariş yazımının TEK transaction'daki ortak sonucu.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Sipariş yerleştirmenin sonucu. İkisi tek bir işlemdir: <see cref="OrderId"/> doluysa hem
/// stok düşülmüş hem sipariş yazılmıştır; <see cref="Stock"/> başarısızsa hiçbiri olmamıştır.
/// "Stok düşüldü ama sipariş yok" diye bir ara durum temsil edilemez — kasıt budur.
/// </summary>
public sealed record OrderPlacementResult(string? OrderId, StockDeductionResult Stock)
{
    public static OrderPlacementResult Placed(string orderId) =>
        new(orderId, StockDeductionResult.Ok());

    public static OrderPlacementResult OutOfStock(StockDeductionResult stock) =>
        new(null, stock);
}
