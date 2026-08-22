# StockDeduction

**Dosya:** `Postgres/StockDeduction.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Tip:** `internal static class` (yalnızca bu proje içinden çağrılır, port yok)

## 1. Ne İşe Yarar

Çok satırlı bir siparişin stok düşümünü, **çağıranın transaction'ı içinde** çalışan atomik bir çekirdek olarak yapar. Kendi transaction'ını açmaz — bilinçli bir tasarım kararı (bkz. §5).

## 2. Hangi Amaçla Kullanılır

Yalnızca [`OrderRepository.PlaceOrder`](OrderRepository.md) tarafından çağrılır: sipariş satırları yazılmadan önce stoğun düşülmesi ve stok yetersizse tüm işlemin (sipariş dahil) rollback edilmesi için.

## 3. Sorumlulukları

- Üstlendiği: satır başına koşullu `UPDATE ... WHERE stock >= qty` ile atomik stok düşümü, yetersiz stok durumunda ayrıntılı (`StockShortage`) raporlama, deadlock'tan kaçınmak için deterministik kilitleme sırası.
- Üstlenmediği: transaction açma/commit etme (çağıranın işi), sipariş yazma.

## 4. İlişkiler

- Yalnızca [`OrderRepository`](OrderRepository.md) içinden, aynı `CustomerSupportDbContext`/transaction ile çağrılır.
- Domain'deki `StockDeductionResult`, `StockShortage`, `OrderLine` modellerini kullanır.

## 5. Tasarım Yaklaşımı

> 🐞 **Neden kendi transaction'ını açmaz:** Stok düşümü ile sipariş satırının yazılması ayrı transaction'larda yapılsaydı, aralarında oluşan herhangi bir hata (DB kesintisi, retry tükenmesi, pod'un ölmesi) stoğu düşülmüş ama karşılığında hiçbir sipariş oluşmamış bir durumda bırakırdı — kimse hata görmez, ürün stoğu sessizce ve kalıcı olarak azalır. Bu yüzden düşüm, kendisini anlamlı kılan sipariş yazmayla **aynı** transaction'da olmak zorunda; bu da transaction sahipliğinin çağıranda kalmasını gerektirir.

> 🐞 **Neden satırlar ürün adına göre sıralı işlenir:** Kozmetik değil — iki eşzamanlı sipariş aynı iki ürünü ters sırada kilitlerse (A sonra B / B sonra A) Postgres deadlock verir. Sabit (alfabetik) sıra, kilitleme düzenini tüm eşzamanlı isteklerde deterministik hale getirip deadlock'u yapısal olarak imkânsızlaştırır.

Düşüm mantığı, projede tekrar eden "conditional claim" desenini kullanır: `ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock - qty))` ile `WHERE Stock >= qty` koşulunu TEK bir atomik SQL ifadesinde birleştirir — "önce oku, sonra yaz" yarışına düşmez (bkz. aynı desenin approval/refresh-token kullanımı: [PostgresApprovalQueue](PostgresApprovalQueue.md), [EfRefreshTokenRepository](../EfCore/Auth/EfRefreshTokenRepository.md)).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static StockDeductionResult TryDeduct(CustomerSupportDbContext ctx, IReadOnlyList<OrderLine> lines, ILogger logger)` | Satırları isme göre sıralı gezer, her biri için koşullu `ExecuteUpdate` çalıştırır; herhangi bir satır yetersizse tüm satırlar için toplanan `StockShortage` listesiyle `Insufficient` döner, hepsi başarılıysa `Ok()` döner. Çağıran, `Insufficient` durumunda transaction'ı commit ETMEMELİDİR. |

## 7. Bağımlılıklar

Yok — parametre olarak verilen `CustomerSupportDbContext` ve `ILogger` üzerinden çalışır, kendi DI kaydı yoktur (static sınıf).

## Bağlantılar

- [OrderRepository](OrderRepository.md) — tek çağıran
- [PostgresApprovalQueue](PostgresApprovalQueue.md) — aynı "koşullu atomik güncelleme" deseninin başka bir kullanımı
