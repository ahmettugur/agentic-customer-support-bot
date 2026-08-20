// Postgres/StockDeduction.cs
// Çok satırlı stok düşümünün, ÇAĞIRANIN transaction'ı içinde çalışan çekirdeği.
//
// Kendi transaction'ını AÇMAZ. Sebebi doğrudan bir arıza: stok düşümü ile siparişin
// yazılması ayrı transaction'larda yapıldığında, ikisi arasında oluşan herhangi bir hata
// (DB kesintisi, retry tükenmesi, pod'un ölmesi) stoğu düşülmüş ama karşılığında hiçbir
// sipariş oluşmamış hâlde bırakır. Kimse hata görmez; ürün stoğu sessizce ve kalıcı olarak
// azalır. Bu yüzden düşüm, kendisini anlamlı kılan yazmayla AYNI transaction'da olmalıdır.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

internal static class StockDeduction
{
    /// <summary>
    /// Satırların stoğunu koşullu UPDATE'lerle düşer. Herhangi bir satır yetersizse hiçbir şey
    /// düşülmüş SAYILMAZ — çağıran transaction'ı commit etmemelidir.
    ///
    /// <para>
    /// Satırlar ürün adına göre sıralı işlenir. Bu kozmetik değil: iki eşzamanlı sipariş aynı
    /// iki ürünü ters sırada kilitlerse Postgres deadlock verir. Sabit sıra kilitleme düzenini
    /// deterministik yapar.
    /// </para>
    /// </summary>
    public static StockDeductionResult TryDeduct(
        CustomerSupportDbContext ctx, IReadOnlyList<OrderLine> lines, ILogger logger)
    {
        var shortages = new List<StockShortage>();

        foreach (var line in lines.OrderBy(l => l.Product, StringComparer.Ordinal))
        {
            var qty = line.Quantity;
            var affected = ctx.Products
                .Where(p => p.Name == line.Product && p.Stock >= qty)
                .ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock - qty));

            if (affected > 0) continue;

            // Düşüm başarısız — sebebini kullanıcıya söyleyebilmek için mevcut stoğu oku.
            // Ürün hiç yoksa Available=0 raporlanır; ürünün varlığı zaten çağrıdan önce
            // FindProduct ile doğrulanmış olmalı, bu yalnızca yarış durumu için savunmadır.
            var available = ctx.Products
                .Where(p => p.Name == line.Product)
                .Select(p => (int?)p.Stock)
                .FirstOrDefault() ?? 0;

            shortages.Add(new StockShortage(line.Product, qty, available));
        }

        if (shortages.Count > 0)
        {
            logger.LogInformation(
                "Stok düşümü tamamı geri alındı — yetersiz satır sayısı: {Count}", shortages.Count);
            return StockDeductionResult.Insufficient(shortages);
        }

        return StockDeductionResult.Ok();
    }
}
