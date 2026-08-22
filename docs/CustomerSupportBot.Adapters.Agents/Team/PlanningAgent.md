# PlanningAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/PlanningAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`PlanningAgent`, Microsoft Agents Framework (MAF) iş akışında ilk adımı yürüten; kullanıcı talebini, geçmişi ve varlık ipuçlarını (`order_id MEVCUT` vb.) analiz ederek `ChatResponseFormat.ForJsonSchema<PlanningResult>` ile strict JSON şemasında bir plan ([PlanningResult](../../CustomerSupportBot.Domain/Model/PlanningResult.md)) üreten ve akışı uygun uzman ajana yönlendiren orkestratör ajandır. Hiçbir aracı (tool) yoktur.

## Hangi amaçla kullanılır`?

- Kullanıcının niyetini (`DetectedIntent`) belirlemek.
- Görevi üstlenecek uzman ajanı (`SelectedAgent`: "ProductAgent", "OrderAgent", "ComplaintAgent", "HumanHandoffAgent") seçmek.
- Bilgi eksikliği varsa kullanıcıya netleştirme sorusu sorulup sorulmayacağını (`NeedsClarification`) ve netleştirme sorusunu (`ClarificationQuestion`) üretmek.
- Çıktıyı `PlanRoutingStrategy`'nin deterministik olarak okuyabileceği hatasız JSON formatında sunmak.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/planning-agent` sistem istemini yüklemek.
  - LLM çıktısını `PlanningResult` JSON şemasına zorlamak.
  - Hata ayıklama (`OnBeforeRun`, `OnAfterRun`) hook'larında üretilen planı yakalamak.

## Constructor ve Başlatma Mantığı

```csharp
public PlanningAgent(IChatClient chatClient, IPromptRepository prompts)
    : base(BuildInner(chatClient, prompts))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak `ChatClientAgent` nesnesini yapılandırır ve `SupportAgentBase` temel sınıfına devreder.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
```
- **Ne işe yarar?:** Planlama ajanı için MAF `ChatClientAgent` örneğini kurar.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.Planning` ("PlanningAgent") atanır.
  2. `Instructions`: `prompts.Get("agents/planning-agent")` ile yüklenir.
  3. `ResponseFormat`: `ChatResponseFormat.ForJsonSchema<PlanningResult>(PlanningSchemaJsonOptions)` atanarak modelin strict JSON üretmesi sağlanır.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** LLM'e gönderilen tam mesaj listesini (kullanıcı sorgusu, entity hint, bağlam) inceler (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** LLM'in ürettiği plan JSON'unu (`response.Text`) yakalar (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- [PlanningResult](../../CustomerSupportBot.Domain/Model/PlanningResult.md)
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
- `Microsoft.Extensions.AI.IChatClient`
