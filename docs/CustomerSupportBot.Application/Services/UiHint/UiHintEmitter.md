# UiHintEmitter

- **Kaynak:** `Services/UiHint/UiHintEmitter.cs`
- **Tür:** `public sealed class : IUiHintEmitter`
- **Namespace:** `CustomerSupportBot.Application.Services.UiHint`

## 1. Ne İşe Yarar

Oturum (session) ID bazlı, bellek içi bir UI-ipucu kuyruğu: bir tool (ör.
[`ProductToolsService.ShowCategoryPicker`](../Tools/ProductToolsService.md)) çalışırken
"kullanıcıya şu görsel bileşeni göster" isteği bırakır (`Emit`), sohbet katmanı bu ipucuları
turun sonunda toplu olarak alıp (`DrainPending`) frontend'e event olarak gönderir.

## 2. Hangi Amaçla Kullanılır

Tool metotlarının (senkron, `StreamEvent` üretme yeteneği olmayan) dolaylı olarak Blazor
istemcisine "bir kategori seçim ekranı göster" gibi zengin UI ipuçları gönderebilmesini
sağlamak — tool'un kendisi doğrudan bir SSE/WebSocket bağlantısına erişmez, bu emitter aradaki
köprüdür.

## 3. Sorumlulukları

**Üstlendiği:**
- `Emit` — ambient bağlamdaki (`IApprovalContextAccessor`) oturum kimliğini okuyup event'i o
  oturumun kuyruğuna eklemek; event'i **üretildiği anda** çalışan ajanın adıyla etiketlemek.
- `DrainPending` — bir oturumun kuyruğundaki tüm bekleyen ipuçlarını atomik biçimde boşaltmak.

**Üstlenmediği:** İpucunun gerçekten frontend'e iletilmesi (o, `DrainPending`'i çağıran sohbet
katmanının/SSE altyapısının işi) — bu sınıf yalnızca bellek içi bir kuyruktur.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IApprovalContextAccessor` — `Emit` anında hangi oturuma/hangi ajana ait olduğunu okumak
  için (SDK/tool çağrısı içinden de güvenilir çalışması için ambient context kullanılır, bir
  parametre olarak geçirilmez).
- Tüketicileri: `ProductToolsService.ShowCategoryPicker` (yayınlayan taraf), sohbet
  orkestrasyon katmanı (`DrainPending`'i çağıran taraf).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Oturum kimliği neden parametre değil, ambient context'ten okunur

Tool metotları (MAF fonksiyon imzaları) genelde `session`/`sessionId` parametresi almaz —
LLM'in çağırdığı imza sabit, ekstra bir "session" parametresi eklemek LLM'in tool şemasını
kirletirdi. `IApprovalContextAccessor.Context.SessionId`, tool çağrısı sırasında zaten
ambient olarak mevcut (aynı context `ApprovalGateService` tarafından da kullanılır) — bu
emitter aynı mekanizmayı yeniden kullanır.

### `Emit`'in `bool` dönmesi neden önemli

Dönüş değeri, ipucunun **gerçekten** kuyruğa eklenip eklenmediğini (yani bir oturum
bağlamının mevcut olup olmadığını) bildirir. Bu, [`ProductToolsService.ShowCategoryPicker`](../Tools/ProductToolsService.md)'ın
"ekran gösterildi mi, gösterilemedi mi" ayrımını yapıp LLM'e buna göre farklı bir talimat
vermesini sağlayan **kritik** bir sinyaldir — sesli (native realtime) kanalda ambient session
bağlamı kurulmadığından `Emit` `false` döner ve model, kategorileri kendisi sesli olarak okur.

### Event'in `agent` alanıyla etiketlenmesi

`TagAgent`, event'i ürettiği anda ambient bağlamdaki "şu an çalışan ajan" bilgisiyle
etiketler. Böylece frontend, hint'in hangi ajana ait olduğunu drain zamanlamasına/sırasına
(ör. o an ekranda görünen son "agent" event'ine) güvenerek **tahmin etmek zorunda kalmaz** —
bilgi doğrudan payload'ın içinde taşınır.

### `DrainPending` neden kuyruğu tüketirken oturumu da temizler

Kuyruk boşaldıktan sonra `_store`'dan da kaldırılır (`TryRemove`) — aksi halde uzun süredir
aktif olmayan binlerce oturum için boş `ConcurrentQueue` nesneleri bellekte sonsuza kadar
birikirdi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Emit(evt)` | Ambient context'ten `sessionId` okur; yoksa `false` döner (ipucu düşürülür). Varsa event'i `TagAgent` ile etiketleyip o oturumun kuyruğuna ekler, `true` döner. |
| `TagAgent(evt, agentName)` *(private static)* | Event verisini JSON nesnesine çevirip `"agent"` alanını ekler; veri yoksa veya ajan adı boşsa event'i olduğu gibi bırakır. |
| `DrainPending(sessionId)` | Kuyruktaki tüm event'leri sırayla çıkarır, listeye ekler; kuyruk boşalınca oturumu store'dan tamamen kaldırır. Kuyruk yoksa/boşsa boş liste döner. |

## 7. Bağımlılıklar

Constructor injection ile: `IApprovalContextAccessor`.

## Bağlantılar

- [../Tools/ProductToolsService.md](../Tools/ProductToolsService.md) — en somut tüketici (kategori seçim ekranı)
