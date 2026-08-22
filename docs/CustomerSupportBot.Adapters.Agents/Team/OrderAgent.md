# OrderAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/OrderAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`OrderAgent`, e-ticaret sipariş süreçlerine ilişkin sorgulama, sepet/sipariş oluşturma, sipariş iptali ve iade talebi işlemlerini yürüten uzman ajandır. Tüm araçlarını [ApprovalGateService](../ApprovalGateService.md) üzerinden temin eder; müşteri kimliği LLM parametresi olarak değil, doğrulanmış oturumdan (`CurrentCustomerId`) otomatik enjekte edilir.

## Hangi amaçla kullanılır`?

Müşterinin mevcut siparişlerini takip etmesini (`order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`), yeni sipariş vermesini (`create_order_tool`), siparişini iptal etmesini (`order_cancel_tool`) veya iade talebi açmasını (`return_request_tool`) sağlamak; yan etkili işlemleri HITL onay kapısıyla güvenceye almak ve çıktısını [SpecialistReasoningSchema](SpecialistReasoningSchema.md) ile yapılandırılmış ReAct JSON olarak üretmek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/order-agent` sistem talimatlarını bağlamak.
  - 6 adet sipariş aracını `ApprovalGateService` üzerinden entegre etmek.
  - `ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>` ile ReAct çıktısını (`preToolCheck`, `postToolReflection`) şemaya zorlamak.
  - Hata ayıklama noktalarında (`OnBeforeRun`, `OnAfterRun`) araç çağrılarını ve sonuçlarını izlemek.

## Constructor ve Başlatma Mantığı

```csharp
public OrderAgent(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate)
    : base(BuildInner(chatClient, prompts, approvalGate))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak `ChatClientAgent` nesnesini oluşturur ve temel sınıf olan [SupportAgentBase](SupportAgentBase.md)'e aktarır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate)
```
- **Ne işe yarar?:** Sipariş ajanının MAF `ChatClientAgent` örneğini yapılandırır.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.Order` ("OrderAgent") olarak atanır.
  2. `Instructions`: `prompts.Get("agents/order-agent")` ile yüklenir.
  3. `Tools`: `approvalGate` üzerinden 6 araç bağlanır (`BuildOrderPlacementTool`, `BuildOrderStatusTool`, `BuildGetLastOrderTool`, `BuildGetAllOrdersTool`, `BuildOrderCancelTool`, `BuildReturnRequestTool`).
  4. `ResponseFormat`: `SpecialistReasoningSchema` camelCase JSON şeması atanır.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** LLM çağrısı öncesi ajana iletilen mesaj geçmişini, sipariş ID ipuçlarını (`order_id MEVCUT`) ve sistem prompt'unu yakalar (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** LLM yanıtını yakalar; çağrılan araçları (`ToolCalls`), araçların döndüğü sonuçları (`ToolResults`) ve üretilen ReAct JSON metnini (`response.Text`) inceler (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- [ApprovalGateService](../ApprovalGateService.md)
- [SpecialistReasoningSchema](SpecialistReasoningSchema.md)
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
