# IUiHintEmitter

**Kaynak:** `Ports/Outbound/IUiHintEmitter.cs`
**Implementasyon:** [`UiHintEmitter`](../../Services/UiHint/UiHintEmitter.md)

## 1. Ne İşe Yarar

Tool fonksiyonlarından streaming pipeline'a UI ipuçları (`StreamEvent`) gönderen port —
örneğin bir ürün listesi tool'u, sonucun yanında "bunu bir kart listesi olarak göster" gibi bir
ipucu ekleyebilir.

## 2. Hangi Amaçla Kullanılır

`ProductToolsService.ProductListTool` gibi tool'lar, çalıştıkları sırada `Emit` çağırarak
frontend'e ek bir UI ipucu gönderir; bu ipuçları `DrainPending` ile turun sonunda toplanıp
stream'e eklenir.

## 3. Sorumlulukları

- **Üstlendiği:** Session bazlı ipucu kuyruklama ve boşaltma.
- **Üstlenmediği:** Session kimliğinin nasıl bilineceği —
  [`IApprovalContextAccessor`](IApprovalContextAccessor.md)'dan otomatik okunur, çağıranın
  session id geçirmesi gerekmez.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/UiHint/UiHintEmitter` implemente eder; `Session ID` tabanlı
`ConcurrentDictionary` kullanır — `AsyncLocal` YERİNE `IApprovalContextAccessor` üzerinden
session ID okur, çünkü MAF'ın SDK'sı (Semantic Kernel tabanlı workflow) `AsyncLocal`
akışını her zaman güvenilir şekilde korumaz; ambient context erişimi bu yüzden port üzerinden
yapılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Emit`'in dönüş değeri (`bool`) kozmetik değildir: ipucu kuyruğa girdiyse `true`, ambient
bağlamda session olmadığı için düştüyse `false`. Bu ayrım önemlidir çünkü ipucu düştüğünde
ekranda hiçbir şey belirmez, dolayısıyla çağıran tool LLM'e "kullanıcıya gösterildi" diyemez.
Sesli (native realtime) kanalda ambient bağlam hiç kurulmadığı için bu yol gerçekten yürünür —
bkz. `ProductToolsService.ProductListTool`, dönüş değerine göre farklı bir metin üretir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `bool Emit(StreamEvent evt)` | Mevcut session'ın kuyruğuna bir ipucu ekler; ambient session yoksa `false`. |
| `IReadOnlyList<StreamEvent> DrainPending(string sessionId)` | Bekleyen tüm ipuçlarını okuyup kuyruğu temizler. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Application.Ports.Inbound.StreamEvent`'e bağımlıdır.
