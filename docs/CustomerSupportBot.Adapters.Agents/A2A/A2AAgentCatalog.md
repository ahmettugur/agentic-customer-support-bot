# A2AAgentCatalog

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.A2A`

## Ne işe yarar?

`A2AAgentCatalog`, A2A (Agent-to-Agent) protokolü üzerinden dış sistemlerle entegre çalışan ve dış dünyaya açılan özel `ProductInfoAgent`, `OrderInfoAgent` ve `ComplaintInfoAgent` ajanlarını kuran katalog sınıfıdır. Bu ajanlar, iç sohbet/sesli kanaldaki `Team/` ajanlarından tamamen ayrıdır; iç JSON şeması (`SpecialistReasoningSchema`) sızdırmaz ve doğrudan düz metin üretirler.

## Hangi amaçla kullanılır`?

- **Güvenli Dış Entegrasyon:** Dış partnerlerin ve üçüncü parti sistemlerin ürün, sipariş ve şikayet bilgilerine erişmesini sağlamak.
- **Salt-Okunur Güvenlik Bariyeri (`ReadOnlyOnly`):** A2A kanalına yazma/yan etkili araçların (sipariş oluşturma, iptal, şikayet kaydı) kesinlikle sızmamasını çalışma zamanında `ReadOnlyOnly` ile denetlemek.
- **Girdi Boyutu Koruması (`InputLimitedAgent`):** Aşırı uzun metin veya parça sayısına sahip kötü niyetli istekleri LLM'e gitmeden reddetmek.
- **Birleşik Telemetri:** A2A çağrılarını iç ajanlarla aynı OpenTelemetry `ActivitySource` ("CustomerSupportBot") üzerine yazarak izlenebilirlikte kör nokta oluşmasını engellemek.

## Sorumlulukları

- **Üstlendiği:**
  - `Product`, `Order`, `Complaint` salt-okunur A2A ajanlarını kurmak.
  - Ajanları önce [InputLimitedAgent](InputLimitedAgent.md), ardından OpenTelemetry ile sarmalamak (`Guarded`).
  - Yan etkili araç verilmeye çalışılırsa `InvalidOperationException` fırlatmak (`ReadOnlyOnly`).

## Constructor ve Başlatma Mantığı

```csharp
public A2AAgentCatalog(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate,
    ICustomerSupportToolsService tools,
    IOptions<A2AOptions> a2aOptions)
```

### Constructor İçerisinde Yapılan İşler:
1. **A2A Sınırlarının Alınması:** `a2aOptions.Value` okunur.
2. **`Product` Ajanının Kurulması:** `agents/a2a-product-agent` talimatları ve `product_inquiry_tool`, `product_list_tool` araçlarıyla `ChatClientAgent` oluşturulur; `Guarded` sarmalamasından geçirilir.
3. **`Order` Ajanının Kurulması:** `agents/a2a-order-agent` talimatları ve salt-okunur `OrderStatusTool`, `GetLastOrderTool`, `GetAllOrdersTool` araçlarıyla kurulur; `Guarded` sarmalamasından geçirilir.
4. **`Complaint` Ajanının Kurulması:** `agents/a2a-complaint-agent` talimatları ve salt-okunur `ComplaintStatusTool`, `GetAllComplaintsTool` araçlarıyla kurulur; `Guarded` sarmalamasından geçirilir.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Guarded` (Private Static)
```csharp
private static AIAgent Guarded(A2AOptions limits, AIAgent agent)
```
- **Ne işe yarar?:** Dış ajanı önce girdi sınırlayıcı (`InputLimitedAgent`), ardından OpenTelemetry ile sarar.
- **Tasarım Kararı:** Sınır sarmalayıcısı telemetrinin içinde yer alır; böylece reddedilen çağrılar da span üretir ve kötüye kullanım loglarda izlenebilir.

### 2. `ReadOnlyOnly` (Internal Static)
```csharp
internal static IList<AITool> ReadOnlyOnly(params AIFunction[] functions)
```
- **Ne işe yarar?:** Verilen fonksiyonlar arasında `WellKnown.SideEffectToolOwners` listesinde tanımlı yan etkili bir araç olup olmadığını denetler.
- **İç Mantığı:** Yan etkili bir araç bulunursa anında `InvalidOperationException` fırlatır.

## Özellikler (Properties)

| Özellik | Tür | Açıklama |
|---|---|---|
| `Product` | `AIAgent` | Dış sistemlere açık, salt-okunur ürün kataloğu ajanı (`ProductInfoAgent`). |
| `Order` | `AIAgent` | Dış sistemlere açık, salt-okunur sipariş bilgisi ajanı (`OrderInfoAgent`). |
| `Complaint` | `AIAgent` | Dış sistemlere açık, salt-okunur şikayet bilgisi ajanı (`ComplaintInfoAgent`). |

## Bağımlılıklar

- `Microsoft.Agents.AI.AIAgent`
- [ApprovalGateService](../ApprovalGateService.md)
- [InputLimitedAgent](InputLimitedAgent.md)
- `CustomerSupportBot.Application.Services.A2A.A2AOptions`
