# ApprovalPortService

**Dosya:** `Services/Approval/ApprovalPortService.cs`  
**Implements:** `IApprovalPort`

## 1. Ne İşe Yarar

HITL onay operasyonlarını orkestre eder — bekleyen onayları listeleme, onaylama, reddetme. `IApprovalQueue` ile `ApprovalExecutionRouter` arasında köprü.

## 2. Hangi Amaçla Kullanılır

Admin panelinin "Bekleyen Onaylar" sayfası bu servisi kullanır.

## 3. Metotlar

| Metot | Açıklama |
|-------|----------|
| `GetPendingAsync(ct)` | Bekleyen onaylar, müşteri adı doldurulmuş olarak |
| `GetRecentAsync(count, ct)` | Son N karar, müşteri adı doldurulmuş olarak |
| `Get(id)` | Tek kayıt (senkron — karar akışında kullanılır, ad gerekmez) |
| `DecideAsync(id, approved, decidedBy, reason, ct)` | Admin kararını uygular; kayıt `Pending` değilse `false` (idempotent) |

## 4. Neden liste metotları async?

Kuyruğun kendisi bellek içi cache'ten gelir — orada I/O yoktur. Async olmalarının tek sebebi
**müşteri adı zenginleştirmesidir** (`EnrichCustomerNamesAsync`).

### Zenginleştirmenin üç kuralı

**Okuma anında çözülür, saklanmaz.** `ApprovalRequest.CustomerName` kalıcı bir alan değildir.
Kayıt anında yazılsaydı müşteri adını değiştirdiğinde panelde eski ad donar ve düzeltmek bir
migration gerektirirdi. Ad, onayın bir parçası değil bir *görüntüleme* alanıdır.

**Tek toplu sorgu.** `ICustomerRepository.GetFullNamesAsync` bir kimlik listesi alır. Kart başına
ayrı sorgu (N+1), kuyruk büyüdükçe panelin açılışını doğrusal olarak yavaşlatırdı.

**Sessiz düşüş.** Müşteri silinmişse, kimlik sayısal değilse veya sorgu hata verirse alan `null`
kalır ve panel yalnızca numarayı gösterir. Müşteri tablosundaki bir arıza HITL kuyruğunu
açılmaz hâle getirmemelidir — bu yüzden istisna yutulur ve `LogWarning` ile kaydedilir.

> ⚠️ **Bilinçli yan etki:** `IApprovalQueue` cache'teki *canlı* nesneleri döndürür, yani ad yazımı
> paylaşılan örnekleri değiştirir. Zararsızdır — değer aynı müşteri için hep aynıdır, kalıcılık
> eşlemesinde yer almaz, ve ikinci çağrıda tekrar sorgulanmayı önleyerek fiilen memoizasyon
> görevi görür. Adı değişen müşteri, kayıt cache'ten düştüğünde veya uygulama yeniden
> başladığında güncellenir.

Bu tasarım `ApprovalRequest.ReasonRequired` ile aynı ruhtadır: panelin ihtiyacı olan türetilmiş
bilgi sunucuda hesaplanır, panel kendi kopyasını tutmaz ve senkron kayması olamaz.

Davranış `ApprovalPortServiceTests` ile korunur (ad doldurma, tek sorgu, bilinmeyen müşteri,
sayısal olmayan kimlik, repository hatası).

## Bağlantılar

- [ApprovalExecutionRouter.md](ApprovalExecutionRouter.md) — Onay sonrası tool yürütme
- [../../CustomerSupportBot.Domain/Model/ApprovalRequest.md](../../CustomerSupportBot.Domain/Model/ApprovalRequest.md) — Onay kaydı
- [../../CustomerSupportBot.Web/Pages/Admin.md](../../CustomerSupportBot.Web/Pages/Admin.md#onay-kartı--bilgi-hiyerarşisi) — Bu verinin çizildiği onay kartı
