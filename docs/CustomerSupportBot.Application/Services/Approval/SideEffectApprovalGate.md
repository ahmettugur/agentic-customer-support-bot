# SideEffectApprovalGate

**Kaynak:** `Services/Approval/SideEffectApprovalGate.cs`
**Tür:** `public sealed class`
**Namespace:** `CustomerSupportBot.Application.Services.Approval`

## 1. Ne İşe Yarar

Yan etkili bir tool çağrısını (sipariş oluşturma, iptal, iade, şikayet kaydı) insan onayına
gönderir ya da — yapılandırmaya göre onay gerekmiyorsa — doğrudan çalıştırır. Yazılı ve sesli
kanalın **ortak tek kaynağıdır**.

## 2. Hangi Amaçla Kullanılır

- **Yazılı sohbet:** [`ApprovalGateService`](../../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md)
  tool lambda'larından çağırır (bağlamı ambient `IApprovalContextAccessor`'dan verir).
- **Sesli görüşme:** [`RealtimeNativeService`](../Realtime/RealtimeNativeService.md) yan etkili
  tool çağrılarında çağırır (bağlamı oturum + bu turun transkriptinden kurar).

## 3. Sorumlulukları

- **Üstlendiği:** onay gerekip gerekmediğine karar vermek (`ApprovalOptions.Enabled` +
  `ToolsRequiringApproval`); ön kontrolü (`preflight`) onay kaydından ÖNCE çalıştırmak; onay
  kaydını oluşturmak; parametre imzasını üretmek (dedup); tool → ajan eşlemesi.
- **Üstlenmediği:** onaylanan işin yürütülmesi — admin kararından sonra
  [`ApprovalExecutionRouter`](ApprovalExecutionRouter.md) yapar; kuyruk kalıcılığı (`IApprovalQueue`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IApprovalQueue` — onay kaydı (`CreateAsync`; bekleyen aynı imzalı kayıt varsa onu döner).
- `ApprovalOptions` — hangi tool'lar onaya tabi.
- DI'da singleton kayıtlıdır (sesli servis kullanır); `ApprovalGateService` aynı sınıfı kendi
  bağımlılıklarıyla içeride kurar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu mantık eskiden yalnızca yazılı sohbetin tool kurucusunda (`Adapters.Agents`) private bir
metottu. Sesli servis Application katmanında olduğu için ona erişemiyordu; sonuç olarak yan etkili
işlemler sesli kanalda tamamen kapatılmıştı. Kural Application katmanına taşınınca iki kanal aynı
kapıyı kullanır: **hangi kanaldan gelirse gelsin yan etkili her işlem aynı insan onayından geçer.**

- **Bloklamaz:** onay gerekiyorsa kayıt oluşturulur ve KARAR BEKLENMEDEN `ToolResult.Pending`
  ("Talebiniz onaya gönderildi…") döner.
- **Ön kontrol:** baştan başarısız olacağı belli bir talep (sipariş yok, başka müşterinin, bozuk
  satır) kuyruğa düşmez. Yürütme anındaki kontrolün yerine geçmez — durum arada değişebilir.
- **Dedup:** "önce oku sonra yaz" yapılmaz (TOCTOU, çok-pod yarışı); her zaman `CreateAsync`
  çağrılır, dedup DB'deki kısmi unique index'te atomiktir. Parametre anahtarları iki kanalda da
  aynı olduğu için aynı talebin yazılı ve sesli kopyaları tek kayıtta birleşir.
- **İmza:** sıradan bağımsız JSON (kaçışlı, kültürden bağımsız); serileştirilemeyen ya da kolon
  sınırını (1000) aşan parametre kümesi benzersiz imza alır (dedup'tan çıkar, yanlış birleşmez).
- **Fotoğraflar:** `IAttachmentStore` verilmişse, oturumun müşterinin bir mesajla **gönderdiği**
  (`SentAt` dolu) ve henüz bir onaya bağlanmamış fotoğrafları `Parameters["attachmentIds"]`'e eklenir
  ve yeni kayda bağlanır. Fotoğraf bir turda, sipariş numarası sonraki turda gelse de talep fotoğrafı
  taşır; bir onaya bağlanan fotoğraf sonraki ilgisiz onaya taşınmaz; yüklenip gönderilmeyen fotoğraf
  hiçbir onaya girmez. **İmza fotoğraflar eklenmeden önce** hesaplanır — arada yeni fotoğraf yüklendi
  diye aynı talep mükerrer kayıt açmaz. Dedup mevcut kaydı döndürdüyse fotoğraflar bağlanmaz (o
  kaydın parametrelerinde yoklar) ve sıradaki onaya kalır. Yürütücü bu anahtarı yok sayar; okuma/
  bağlama hatası onayı engellemez.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool RequiresApproval(string toolName)` | Onay açık mı ve tool listede mi. |
| `Task<ToolResult> ExecuteAsync(toolName, parameters, executeDirectly, ApprovalContext? context, preflight = null)` | Onay gerekmiyorsa çalıştırır; gerekiyorsa ön kontrol → onay kaydı → `Pending`. |
| `static string ResolveAgentName(string toolName)` | `WellKnown.SideEffectToolOwners` eşlemesi; bilinmeyende `"UnknownAgent"`. |
| `static string BuildParamSignature(IReadOnlyDictionary<string, object?>)` | Dedup imzası. |

## 7. Bağımlılıklar

`IApprovalQueue`, `IOptions<ApprovalOptions>`, `ILogger?`, `IAttachmentStore?` (fotoğraf bağlama; yoksa devre dışı).

## Bağlantılar

- [ApprovalGateService](../../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md) — yazılı kanal
- [RealtimeNativeService](../Realtime/RealtimeNativeService.md) — sesli kanal
- [ApprovalExecutionRouter](ApprovalExecutionRouter.md) — onay sonrası yürütme
