# RealtimeFunctionTools

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Realtime/RealtimeFunctionTools.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Realtime`

## Ne işe yarar?

`RealtimeFunctionTools`, OpenAI Realtime API (`gpt-realtime-1.5`) ses modeline sunulan function calling araç şemalarını (JSON Schema draft 2020-12) tanımlayan katalog sınıfıdır.

## Hangi amaçla kullanılır`?

- Sesli görüşme sırasında modelin çağırabileceği araçları tanımlamak.
- **Güvenlik Kısıtı:** Sadece **salt-okunur** araçlar (`product_inquiry_tool`, `product_list_tool`, `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool` ve `end_conversation`) burada tanımlanır. Yan etkili sipariş oluşturma ve şikayet kaydı gibi araçlar HITL gerektirdiği için sesli modele kesinlikle gösterilmez.
- `OpenAiRealtimeClientAdapter.ConfigureNativeSessionAsync` sırasında `session.update` mesajına `tools` dizisini sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - `GetToolDefinitions` ile JSON Schema formatındaki araç listesini dönmek.
  - `GetToolNames` ile açılan araç adlarını UI tarafına iletmek.
  - Görüşmeyi sonlandırma aracı için `EndConversationToolName` sabitini sunmak.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `EndConversationToolName` | Sabit | `public const string EndConversationToolName = "end_conversation"` | Modelin görüşmeyi bitirme niyetini belirten özel araç adı. |
| `GetToolDefinitions` | Metot | `public IReadOnlyList<object> GetToolDefinitions()` | OpenAI `session.update` için JSON Schema nesnelerini döner. |
| `GetToolNames` | Metot | `public IReadOnlyList<string> GetToolNames()` | Tanımlı tüm realtime araç isimlerini döner. |

## Tanımlı Araç Şemaları

1. **`product_inquiry_tool`**: Ürün adı ile fiyat ve stok sorgular (`product_name` zorunlu).
2. **`product_list_tool`**: Kategori bazlı veya tüm kataloğu listeler (`category` opsiyonel).
3. **`order_status_tool`**: Sipariş durumu ve kargo sorgular (`order_id` zorunlu).
4. **`get_last_order_tool`**: Müşterinin son siparişini sorgular. Parametre almaz — müşteri kimliği oturumdan otomatik alınır.
5. **`get_all_orders_tool`**: Müşterinin tüm siparişlerini listeler. Parametre almaz — müşteri kimliği oturumdan otomatik alınır.
6. **`end_conversation`**: Kullanıcı vedalaştığında görüşmeyi kapatır (`reason` opsiyonel parametresi).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu şemalar bilerek **ayrı, minimal bir katalog sınıfında** tutulur — Application katmanındaki
gerçek tool implementasyonlarıyla (`OrderToolsService` vb.) karışmaz. Sebep: sesli modele
gösterilecek "yüzey alanı" ile metin sohbetindeki tool seti kasıtlı olarak **farklı** —
sesli modda yan etkili (sipariş oluşturma, iptal, iade, şikayet) hiçbir tool tanımlanmaz,
dolayısıyla model bunları çağıramaz (adı bile bilmiyor). Bu, HITL onay akışının sesli
moddan tamamen bypass edilememesini garanti eden bir tasarım kısıtıdır, kod incelemesiyle
değil şema seviyesinde uygulanır.

`GetToolDefinitions()`'ın döndürdüğü şema, `OpenAiRealtimeClientAdapter.ConfigureNativeSessionAsync`
tarafından `session.update` mesajının `tools` alanına aynen kopyalanır (bkz.
[OpenAiRealtimeClientAdapter](OpenAiRealtimeClientAdapter.md)).

## Diğer Katman ve Bileşenlerle İlişkileri

- [`OpenAiRealtimeClientAdapter`](OpenAiRealtimeClientAdapter.md) — `GetToolDefinitions()`'ı `session.update`'e, `GetToolNames()`'i `NativeToolNames` property'sine bağlar.
- Application katmanındaki realtime orkestrasyon servisi — modelin `ToolCallReady` event'inde bildirdiği tool adını gerçek iş mantığına yönlendirir (dispatch burada değil, orada yapılır).
- DI: [AiAdapterServiceCollectionExtensions](../DependencyInjection/AiAdapterServiceCollectionExtensions.md) içinde **Singleton** olarak kaydedilir — şemalar bağlantıdan/oturumdan bağımsız sabit olduğu için her WebSocket bağlantısında yeniden oluşturulmaz.

## Bağımlılıklar

Yok — parametresiz constructor, hiçbir servis inject etmez. Tüm içerik derleme zamanı sabit (`static readonly`) veri.
