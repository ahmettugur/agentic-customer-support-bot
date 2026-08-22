# ProductAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/ProductAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`ProductAgent`, e-ticaret ürün arama, stok durumu, fiyat ve kategori listeleme sorgularını yanıtlayan uzman ajandır. Tüm araçları salt-okunur olduğu için compound sorgularda diğer salt-okunur görevlerle eşzamanlı/paralel olarak çalıştırılabilir (`WellKnown.AgentNames.ReadOnly`).

## Hangi amaçla kullanılır`?

Müşterilerin ürün kataloğu sorgularını (`product_inquiry_tool`, `product_list_tool`) karşılamak, araç öncesinde arama parametrelerini değerlendirmek (`preToolCheck`), araç sonrasında dönen ürün listesini incelemek (`postToolReflection`) ve çıktıyı [SpecialistReasoningSchema](SpecialistReasoningSchema.md) ile yapılandırılmış JSON olarak üretmek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/product-agent` sistem prompt'unu ve ürün araçlarını bağlamak.
  - `ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>` ile ReAct çıktısı üretmek.
  - Breakpoint noktalarında (`OnBeforeRun`, `OnAfterRun`) araç çağrılarını izlemek.

## Constructor ve Başlatma Mantığı

```csharp
public ProductAgent(
    IChatClient chatClient,
    IPromptRepository prompts,
    ICustomerSupportToolsService tools)
    : base(BuildInner(chatClient, prompts, tools))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak ürün aracına sahip `ChatClientAgent` nesnesini oluşturur ve `SupportAgentBase` temel sınıfına aktarır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(
    IChatClient chatClient,
    IPromptRepository prompts,
    ICustomerSupportToolsService tools)
```
- **Ne işe yarar?:** Ürün ajanının MAF `ChatClientAgent` örneğini yapılandırır.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.Product` ("ProductAgent") atanır.
  2. `Instructions`: `prompts.Get("agents/product-agent")` ile yüklenir.
  3. `Tools`: `ProductInquiryTool` ve `ProductListTool` fonksiyonları eklenir.
  4. `ResponseFormat`: `SpecialistReasoningSchema` camelCase JSON şeması atanır.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** Ajanın LLM'e göndereceği mesaj listesini yakalar (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** LLM'in çağırdığı araçları (`ToolCalls`), araçların getirdiği ürün verilerini (`ToolResults`) ve ReAct JSON metnini inceler (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- [SpecialistReasoningSchema](SpecialistReasoningSchema.md)
- [ICustomerSupportToolsService](../../CustomerSupportBot.Application/Ports/Outbound/ICustomerSupportToolsService.md)
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
