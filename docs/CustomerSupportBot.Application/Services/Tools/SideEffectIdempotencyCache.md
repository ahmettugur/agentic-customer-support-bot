# SideEffectIdempotencyCache (+ IdempotentCall)

- **Kaynak:** `Services/Tools/SideEffectIdempotencyCache.cs`
- **Tür:** `public sealed class` + `public sealed record IdempotentCall(ToolResult Result, string? EntityId, DateTimeOffset RecordedAt)`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## 1. Ne İşe Yarar

Yan etkili tool çağrıları (sipariş oluşturma, şikayet kaydı) için **kısa pencereli** (varsayılan
60sn) mükerrer-çağrı koruması. Thread-safe; paralel alt görevlerden eş zamanlı çağrılabilir.

## 2. Hangi Amaçla Kullanılır

Aynı parametrelerle kısa süre içinde tekrar gelen bir sipariş/şikayet oluşturma çağrısının
**ikinci bir kayıt** yaratmasını önlemek — hem LLM'in kendi kendini tekrarlaması hem istemci
tarafı çift gönderim hem de compound query'lerin paralel alt görevleri için.

## 3. Sorumlulukları

**Üstlendiği:** Parametre-imzası tabanlı (SHA-256 hash) cache; süresi dolan kayıtları temizlemek
(`PruneExpired`); kapasite aşımında en eski kayıtları atmak (FIFO).

**Üstlenmediği:** Cache'in ne zaman kontrol edileceğine karar vermek — bu, çağıran tool
metodunun (`OrderToolsService.OrderPlacementTool`, `ComplaintToolsService.ComplaintRegistrationTool`)
işidir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Tüketicileri: [`OrderToolsService`](OrderToolsService.md) (`OrderPlacementTool`),
  [`ComplaintToolsService`](ComplaintToolsService.md) (`ComplaintRegistrationTool`).
- `ToolResult` (Domain) — cache'lenen sonuç tipi.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Neden gerekli — mevcut `MaxDuplicateToolCalls` guard'ı yetmiyor

`MaxDuplicateToolCalls` guard'ı `CustomerSupportChatManager` içinde, yani **tek bir** workflow
koşusunun mesaj geçmişine bakar. Compound query'de her alt görev ayrı bir workflow koşusu
olarak (bazen **paralel**) çalıştığı için o guard mükerrer `order_placement_tool`/
`complaint_registration_tool` çağrılarını göremez. Bu cache, süreç genelinde (tüm workflow
koşularını kapsayan) son N saniyedeki aynı-parametreli çağrıyı yakalar.

### Cache isabetinde davranış — sessiz taklit değil, açık bildirim

Cache isabetinde tool **çalıştırılmaz** (DB'ye yazılmaz), ama ilk sonuç sessizce tekrar da
döndürülmez — çağırana "bu kaydı az önce oluşturdum" bilgisini içeren ayırt edilebilir bir
sonuç döndürmesi için orijinal kayıt (`IdempotentCall`) geri verilir (bkz.
[OrderToolsService.md](OrderToolsService.md)'deki `duplicate: true` alanı). Böylece hem
mükerrer kayıt engellenir hem meşru bir tekrar talebi (kullanıcı gerçekten ikinci bir sipariş
istiyorsa) fark edilebilir kalır.

### Pod'lar arası katman — `IDistributedIdempotencyStore`

Bellek içi cache **pod başınadır**. Yatay ölçeklendirmede aynı istek (istemci yeniden denemesi,
yük dengeleyicinin isteği başka pod'a yönlendirmesi) ikinci pod'a düştüğünde o pod'un cache'i
boştur ve ikinci bir sipariş/şikayet kaydı oluşuyordu. İsteğe bağlı
[`IDistributedIdempotencyStore`](../../Ports/Outbound/Persistence/IDistributedIdempotencyStore.md)
(üretimde [`RedisIdempotencyStore`](../../../CustomerSupportBot.Adapters.Redis/Idempotency/RedisIdempotencyStore.md),
`SET NX PX`) ikinci katmandır:

- `TryGetRecent` önce yerel cache'e, ıskalarsa dağıtık kayda bakar; bulursa
  `"Kayıt zaten oluşturulmuş: {id}."` sonucuyla bir `IdempotentCall` döndürür.
- `Record` yerel kayda ek olarak dağıtık kayda da yazar (aynı pencere süresiyle, ilk yazan kazanır).
- **Fail-open:** dağıtık kayıt erişilemezse (Redis kesintisi) uyarı loglanır ve yalnızca yerel
  katmanla devam edilir — idempotency kaydının kesintisi sipariş akışını durdurmamalı. Bu
  "en iyi çaba" bir korumadır; kesin tekillik gerektiren durumlarda DB kısıtı gerekir.

Kayıt yoksa (testler, tek süreç) davranış eskisiyle aynıdır.

### Yalnızca başarılı sonuçlar cache'lenir

`Record`, `result.Success == false` ise hiçbir şey yapmaz — hata durumunda tekrar denemenin
engellenmesi istenmez; bir başarısız çağrının ardından gelen düzeltilmiş tekrar denemesi asla
"az önce yaptım" diye reddedilmemeli.

### Kapasite aşımında FIFO, neden LRU değil

Pencere zaten kısa (60sn varsayılan) olduğu için basit "en eski `RecordedAt`'i at" (FIFO)
yeterli — LRU'nun getirdiği ekstra karmaşıklık bu ölçekte gereksiz.

### `BuildKey` neden SHA-256 hash kullanır

Parametreler (tool adı + tüm parametre değerleri) birleştirilip hash'lenir — hem anahtarı
sabit uzunlukta tutar hem de parametre değerlerinin doğrudan bellekte (dictionary anahtarı
olarak) tutulmasını önler.

## 6. Metotlar / Üyeler

### `IdempotentCall` *(record)*

| Alan | Açıklama |
|---|---|
| `Result` | Orijinal çağrının döndürdüğü `ToolResult`. |
| `EntityId` | Oluşturulan kaydın kimliği (sipariş/şikayet numarası) — uyarı mesajında kullanılır. |
| `RecordedAt` | Kaydın alındığı UTC zamanı. |

### `SideEffectIdempotencyCache`

| Üye | Açıklama |
|---|---|
| `DefaultWindow` *(static readonly, `TimeSpan`)* | Varsayılan mükerrer tespit penceresi — 60 saniye. |
| `DefaultMaxEntries` *(const int)* | Bellekte tutulacak maksimum kayıt sayısı — 200. |
| `TryGetRecent(toolName, parameters, out recent)` | Aynı tool aynı parametrelerle pencere içinde çağrılmış mı diye bakar; süresi dolmuş kayıtları önce temizler (`PruneExpired`). Yerelde yoksa dağıtık kayda bakar (varsa; hata → fail-open). |
| `Record(toolName, parameters, result, entityId)` | Başarılı bir çağrıyı kaydeder (başarısızsa no-op); kapasite aşımında en eski kayıtları FIFO ile atar. Dağıtık kayda da yazar (varsa). |
| `Clear()` | Test/oturum sıfırlama için tüm cache'i temizler. |
| `PruneExpired(now)` *(private)* | `_window`'u aşan kayıtları siler; `_gate` kilidi altında çağrılmalı. |
| `BuildKey(toolName, parameters)` *(private static)* | Tool adı + parametreleri birleştirip SHA-256 hash'ler, hex string döner. |

## 7. Bağımlılıklar

Constructor injection ile (hepsi opsiyonel, test edilebilirlik için): `TimeSpan? window`,
`int maxEntries`, `TimeProvider? clock`, `IDistributedIdempotencyStore? distributed`,
`ILogger<SideEffectIdempotencyCache>? logger`.

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md), [ComplaintToolsService.md](ComplaintToolsService.md) — tüketiciler
