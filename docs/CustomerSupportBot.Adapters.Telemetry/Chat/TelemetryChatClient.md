# TelemetryChatClient

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/Chat/TelemetryChatClient.cs`
- **Tür:** `public sealed class : DelegatingChatClient`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.Chat`

## Ne işe yarar?

`TelemetryChatClient`, `Microsoft.Extensions.AI.DelegatingChatClient` sınıfından türeyen; altındaki gerçek `IChatClient` nesnesini sarmalayarak her LLM tamamlama (`GetResponseAsync`) ve akış (`GetStreamingResponseAsync`) çağrısında otomatik olarak OpenTelemetry `ai.chat` span'i açan, harcanan girdi/çıktı token'larını yakalayan, [ICostCalculatorPort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md) ile USD maliyetini hesaplayan ve [CostUsageStore](../OpenTelemetry/CostUsageStore.md) ile OpenTelemetry sayaçlarına yazan telemetri dekoratörüdür.

## Hangi amaçla kullanılır`?

- LLM çağrılarının sürelerini (`Stopwatch`), gecikmelerini ve token harcamalarını şeffaf bir şekilde ölçmek.
- Başarılı çağrılarda token sayılarını, maliyetini ve model adını `Activity` etiketlerine (`ai.tokens.input`, `ai.tokens.output`, `ai.cost.usd`) yazmak (`RecordSuccess`).
- İstisna fırlatıldığında `RecordFailure` ile span durumunu `Error` olarak işaretlemek ve hatayı yeniden fırlatmak.
- Streaming akışlarında `UsageContent` bloklarını yakalayarak akış bittiğinde toplam tüketimi kaydetmek.

## Sorumlulukları

- **Üstlendiği:**
  - `GetResponseAsync` ve `GetStreamingResponseAsync` çağrılarını telemetri ile sarmalamak.
  - `CustomerSupportTelemetry.LlmCallsCounter`, `InputTokensCounter`, `OutputTokensCounter`, `CostUsdCounter` sayaçlarını artırmak.
  - Varsa kalıcı ambar (`ILlmCallPersistencePort`) üzerine `PersistAsync` ile arka planda asenkron kayıt yazmak.
- **Üstlenmediği:**
  - Gerçek LLM çağrısını yapmak — bu iş tamamen `base` (sarmalanan `inner` `IChatClient`) tarafından yürütülür; bu sınıf yalnızca çağrıyı gözlemler.
  - Maliyet formülünü hesaplamak — bu iş [ICostCalculatorPort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md) implementasyonuna (`CostCalculator`) devredilir.

## Constructor ve Başlatma Mantığı

```csharp
public TelemetryChatClient(
    IChatClient inner,
    ICostCalculatorPort costCalculator,
    CostUsageStore usageStore,
    string modelHint,
    string provider,
    ILogger<TelemetryChatClient> logger,
    ILlmCallPersistencePort? persistence = null) : base(inner)
```

### Constructor İçerisinde Yapılan İşler:
- `inner` (`IChatClient`): Sarmalanan asıl chat client `base(inner)` çağrısıyla aktarılır.
- `_costCalculator`: Maliyet hesaplayıcı atanır.
- `_usageStore`: Bellek içi kullanım ambarı atanır.
- `_modelHint`: Varsayılan model adı (ör. `gpt-4o-mini`).
- `_provider`: Sağlayıcı adı (`OpenAI`, `AzureOpenAI`).
- `_persistence`: Opsiyonel kalıcı veritabanı yazıcısı atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetResponseAsync`
```csharp
public override async Task<ChatResponse> GetResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Tekil LLM çağrısını yürütür ve ölçümlerini kaydeder.
- **İç Mantığı:**
  1. `CustomerSupportTelemetry.StartLlmActivity("chat", _modelHint, _provider)` ile span açılır.
  2. `Stopwatch` başlatılır.
  3. `await base.GetResponseAsync(...)` çalıştırılır.
  4. Başarıda `RecordSuccess` çağrılır ve yanıt döndürülür; hatada `RecordFailure` çağrılıp istisna fırlatılır.

### 2. `GetStreamingResponseAsync`
```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Canlı token akışını yürütür.
- **İç Mantığı:** Gelen her `update` parçasındaki `Contents` incelenerek `UsageContent` (token sayıları) aranır; döngü bitiminde `RecordSuccess` ile toplam maliyet yazılır.

### 3. `RecordSuccess` (Private)
- **Ne işe yarar?:** Token sayıları (`input`, `output`), süre (`durationMs`) ve hesaplanan maliyet (`cost`) değerlerini `CustomerSupportTelemetry` sayaçlarına, histogramına, `_usageStore` ambarına ve span etiketlerine yazar.
- **İç Mantığı:** Önce `_costCalculator.CalculateCost` ile maliyet hesaplanır; ardından sayaçlar (`LlmCallsCounter` her zaman, `InputTokensCounter`/`OutputTokensCounter`/`CostUsdCounter` yalnızca değer > 0 ise) artırılır, `_usageStore.Record` çağrılır, `PersistAsync` tetiklenir ve son olarak `Debug` seviyesinde log yazılır.

### 4. `PersistAsync` (Private)
```csharp
private void PersistAsync(string model, long input, long output, decimal cost, double durationMs)
```
- **Ne işe yarar?:** `ILlmCallPersistencePort` enjekte edilmişse (opsiyonel), çağrı kaydını kalıcı depoya (ör. veritabanı) yazar.
- **İç Mantığı:** `_persistence == null` ise hiçbir şey yapmaz. Aksi halde `Task.Run` ile **fire-and-forget** bir arka plan görevi başlatır ve olası istisnayı yutar (persistence katmanının kendi içinde zaten logladığı varsayılır) — böylece kalıcı kayıt yazma gecikmesi veya hatası, kullanıcıya dönen LLM yanıtını asla bloklamaz veya bozmaz.

### 5. `RecordFailure` (Private)
```csharp
private void RecordFailure(Activity? activity, Exception ex)
```
- **Ne işe yarar?:** LLM çağrısı istisna fırlattığında span'i `Error` durumuna işaretler (`activity.SetStatus`, `error.type` tag'i) ve `Warning` seviyesinde log yazar. Sayaçlara herhangi bir değer yazılmaz — başarısız çağrılar `LlmCallsCounter`'a dahil edilmez.

## Bağımlılıklar

- `Microsoft.Extensions.AI.DelegatingChatClient`
- [ICostCalculatorPort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md)
- [CostUsageStore](../OpenTelemetry/CostUsageStore.md)
- [CustomerSupportTelemetry](../OpenTelemetry/CustomerSupportTelemetry.md)
- `CustomerSupportBot.Application.Ports.Outbound.Observability.ILlmCallPersistencePort`
