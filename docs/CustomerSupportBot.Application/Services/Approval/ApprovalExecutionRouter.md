# ApprovalExecutionRouter

**Dosya:** `Services/Approval/ApprovalExecutionRouter.cs`
**Tür:** `public sealed class : IApprovalExecutionRouter`
**Namespace:** `CustomerSupportBot.Application.Services.Approval`

## 1. Ne İşe Yarar

Bir admin onay talebini (sipariş verme, şikayet kaydı, sipariş iptali, iade) **onayladığında**,
o talebin `ToolName` + `Parameters` alanlarını okuyup doğru `ICustomerSupportToolsService`
metoduna yönlendiren küçük bir dispatcher (4 `case`, reflection yok). "Onay verildi" ile
"iş gerçekten yapıldı" arasındaki tek köprüdür.

## 2. Hangi Amaçla Kullanılır

HITL (human-in-the-loop) akışında dört tool (`OrderPlacement`, `ComplaintRegistration`,
`OrderCancel`, `ReturnRequest`) admin onayı gerektirir. Bu tool'lar çağrıldığında **hemen
yürütülmez** — bir `ApprovalRequest` kaydı oluşturulur ve kullanıcıya "talebiniz onaya
gönderildi" cevabı döner (workflow turu burada biter, admin kararını beklemez). Admin daha
sonra panelden onayladığında, kayıt yürütme aşamasına geçer ve **bu sınıf** gerçek
`ICustomerSupportToolsService` çağrısını yapar.

## 3. Sorumlulukları

- **Üstlendiği:** `ToolName` → doğru servis metodu eşlemesi; `ApprovalRequest.Parameters`
  sözlüğünden (hem canlı obje hem Postgres'ten JSON round-trip sonrası `JsonElement` biçiminde
  gelen) tip-güvenli okuma; bilinmeyen `ToolName` için güvenli hata dönüşü.
- **Üstlenmediği:** Onay kararının kendisi (bu [`ApprovalPortService`](ApprovalPortService.md)/
  `IApprovalQueue.DecideAsync`'te), müşteri kimliğinin doğrulanması (bu zaten `ApprovalRequest.CustomerId`
  alanına HITL kaydı oluşturulurken, JWT-doğrulanmış kimlikten yazılmıştır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IApprovalExecutionRouter` port'unu implemente eder.
- **Inject eder:** `ICustomerSupportToolsService` (gerçek iş mantığını çalıştıran servis —
  sipariş/şikayet/iptal/iade tool'larının hepsini tek arayüzde toplar).
- **Kimin tarafından çağrılır:** `IApprovalQueue.DecideAsync` implementasyonları
  (`PostgresApprovalQueue`/`InMemoryApprovalQueue`, Adapters.Persistence katmanı) — onay
  `approved=true` ile karara bağlandığında, karar kalıcı hale gelmeden/olay yayınlanmadan
  ÖNCE bu router `await` edilir ve sonuç `ApprovalRequest.ExecutionResult`'a yazılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden ayrı bir sınıf (reflection değil, 4 elle yazılmış `case`):** Tool sayısı sabit ve az (4);
reflection tabanlı genel bir çözüm (ör. `ToolName` → metot adı eşleşmesi, parametreleri otomatik
bind etme) hem debug etmesi zor hem de bir parametre adı/tipi uyuşmazlığında derleme zamanında
değil çalışma zamanında patlayan bir hata sınıfı açardı. Elle yazılmış `switch`, her tool'un
parametre listesini derleme zamanında `ICustomerSupportToolsService` arayüzüne karşı doğrular.

> 🐞 **`OrderCancel`/`ReturnRequest` için `customerId`, `Parameters` sözlüğünden değil
> `request.CustomerId`'den okunur** — bu bilinçli bir tutarlılık kararı: `ApprovalRequest.CustomerId`,
> HITL kaydı oluşturulduğu anda JWT-doğrulanmış kimlikten yazılan **kanonik** alandır
> (bkz. `ApprovalGateService.ExecuteWithApprovalGateAsync`, Adapters.Agents katmanı). Aynı
> bilgiyi ayrıca `Parameters` sözlüğüne de yazıp oradan okumak, iki kopyanın birbirinden
> sapma riskini (ve bir gün yanlış olanın okunma riskini) gereksiz yere açardı.

**Neden hem canlı obje hem `JsonElement` desteklenir (`GetString`/`GetLines`):** Bir onay talebi
iki farklı kaynaktan gelebilir: (a) süreç yeniden başlamadan, bellekte hâlâ duran canlı bir
`Dictionary<string, object?>` (değerler orijinal C# tipleriyle — ör. `IEnumerable<OrderLineRequest>`);
(b) Postgres'ten `HydrateAsync` ile geri yüklenmiş bir kayıt (`Parameters` bir JSON sütunundan
deserialize edilmiş, değerler `System.Text.Json.JsonElement`'tir). İki kod yolu birbirinden
farklı davranırsa (ör. biri çalışır biri boş sipariş üretir), hata yalnızca **restart sonrası
onaylanan** talepler için ortaya çıkar — nadir ve tespiti zor bir hata sınıfı olurdu. `GetString`/
`GetLines` bu ikiliği tek yerde çözer.

`GetLines`, `productName`/`quantity` alanlarını **büyük/küçük harfe duyarsız** okur çünkü
sözlük anahtarları `ApprovalGateService`'in yazdığı camelCase adlar, JSON tarafında ise
`OrderLineRequest`'in PascalCase property adlarıdır — biçim farkı satırların sessizce boş
dönmesine yol açmamalı. Boş liste dönerse tool zaten `ValidationError` ile reddeder; sessizce
boş bir sipariş oluşmaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ExecuteAsync(ApprovalRequest request, CancellationToken ct = default): Task<ApprovalExecutionOutcome>` | `request.ToolName`'e göre doğru `ICustomerSupportToolsService` metodunu çağırır; bilinmeyen tool için `SystemError` sonucu döner. |
| `GetString(...)` *(private static)* | Sözlükten string alanı hem canlı obje hem `JsonElement` biçiminden okur. |
| `GetLines(...)` *(private static)* | Sipariş satırlarını (`OrderLineRequest` listesi) her iki kaynaktan da okur, büyük/küçük harf duyarsız alan eşlemesi yapar. |
| `ReadProperty(...)` *(private static)* | `JsonElement` üzerinde büyük/küçük harf duyarsız property arama yardımcıdır. |

## 7. Bağımlılıklar (Constructor Injection)

- `ICustomerSupportToolsService` — sipariş/şikayet/iptal/iade işlemlerinin gerçek implementasyonu.

## Bağlantılar

- [ApprovalPortService.md](ApprovalPortService.md) — onay kuyruğu orkestrasyonu
- [ApprovalContextAccessor.md](ApprovalContextAccessor.md) — tur bazlı bağlam taşıyıcı
