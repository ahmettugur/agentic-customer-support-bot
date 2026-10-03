# RealtimeFunctionTools

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Realtime/RealtimeFunctionTools.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Realtime`

## Ne işe yarar?

`RealtimeFunctionTools`, OpenAI Realtime API (`gpt-realtime-2`) ses modeline sunulan function calling araç şemalarını (JSON Schema draft 2020-12) tanımlayan katalog sınıfıdır.

## Hangi amaçla kullanılır`?

- Sesli görüşme sırasında modelin çağırabileceği araçları tanımlamak.
- Yazılı sohbetin iş araçlarının **tamamı** tanımlıdır. Yan etkili araçların (sipariş oluşturma, iptal, iade, şikayet kaydı) açıklamaları modele işlemin hemen gerçekleşmediğini, **insan onayına** gönderildiğini ve çağırmadan önce müşteriden açık onay alması gerektiğini söyler (`ApprovalNote`).
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
6. **`complaint_status_tool`**: Şikayet numarasıyla durum sorgular (`complaint_id` zorunlu).
7. **`get_all_complaints_tool`**: Müşterinin tüm şikayetlerini listeler. Parametre almaz.
8. **`order_placement_tool`** *(onaya gider)*: Yeni sipariş; `lines` dizisi (`product_name`, `quantity` ≥ 1) — çok ürün tek çağrıda.
9. **`order_cancel_tool`** *(onaya gider)*: `order_id`, `reason`.
10. **`return_request_tool`** *(onaya gider)*: `order_id`, `reason` — teslim edilmiş ve 14 gün içindeki siparişler.
11. **`complaint_registration_tool`** *(onaya gider)*: `order_id`, `complaint_text`.
12. **`human_handoff_tool`**: Müşteri insan temsilci istediğinde temsilci kuyruğuna talep açar (`reason` zorunlu).
13. **`end_conversation`**: Kullanıcı vedalaştığında görüşmeyi kapatır (`reason` opsiyonel parametresi).

Hiçbir şemada `customer_id` parametresi yoktur — müşteri kimliği her zaman oturumdaki doğrulanmış kimlikten gelir.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu şemalar bilerek **ayrı bir katalog sınıfında** tutulur — Application katmanındaki gerçek tool
implementasyonlarıyla (`OrderToolsService` vb.) karışmaz; parametre adları OpenAI şema
geleneğiyle `snake_case`'tir ve `RealtimeNativeService` bunları yazılı kanalın parametre
anahtarlarına çevirir.

**HITL garantisi şemada değil kapıdadır.** Eskiden sesli modda yan etkili hiçbir tool
tanımlanmıyor, HITL bu yolla korunuyordu — ama müşteri sesle sipariş/iptal/iade/şikayet
yapamıyordu. Artık tool'lar tanımlı; model onları çağırdığında işlem YAPILMAZ:
`RealtimeNativeService`, yazılı sohbetle aynı `SideEffectApprovalGate` üzerinden onay kaydı
oluşturur. Kapı yoksa (yapılandırma hatası) yan etkili tool `APPROVAL_UNAVAILABLE` ile
reddedilir — doğrudan çalışmaya asla düşmez.

Liste elle tutulduğu için yazılı tarafa yeni bir iş tool'u eklenip buraya eklenmezse
`RealtimeFunctionToolsTests.Voice_ExposesEveryBusinessTool` düşer. (`category_picker` yalnızca
görsel bir kategori seçici olduğu için sesli kanalda yoktur; kategori listesi
`product_list_tool` ile sesli olarak verilir.)

`GetToolDefinitions()`'ın döndürdüğü şema, `OpenAiRealtimeClientAdapter.ConfigureNativeSessionAsync`
tarafından `session.update` mesajının `tools` alanına aynen kopyalanır (bkz.
[OpenAiRealtimeClientAdapter](OpenAiRealtimeClientAdapter.md)).

## Diğer Katman ve Bileşenlerle İlişkileri

- [`OpenAiRealtimeClientAdapter`](OpenAiRealtimeClientAdapter.md) — `GetToolDefinitions()`'ı `session.update`'e, `GetToolNames()`'i `NativeToolNames` property'sine bağlar.
- Application katmanındaki realtime orkestrasyon servisi — modelin `ToolCallReady` event'inde bildirdiği tool adını gerçek iş mantığına yönlendirir (dispatch burada değil, orada yapılır).
- DI: [AiAdapterServiceCollectionExtensions](../DependencyInjection/AiAdapterServiceCollectionExtensions.md) içinde **Singleton** olarak kaydedilir — şemalar bağlantıdan/oturumdan bağımsız sabit olduğu için her WebSocket bağlantısında yeniden oluşturulmaz.

## Bağımlılıklar

Yok — parametresiz constructor, hiçbir servis inject etmez. Tüm içerik derleme zamanı sabit (`static readonly`) veri.
