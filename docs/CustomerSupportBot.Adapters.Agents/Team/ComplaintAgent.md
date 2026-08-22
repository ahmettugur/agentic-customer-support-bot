# ComplaintAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/ComplaintAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`ComplaintAgent`, müşteri şikayetlerini ve memnuniyetsizliklerini karşılayan; şikayet kaydını (`complaint_registration_tool`) [ApprovalGateService](../ApprovalGateService.md) üzerinden HITL onay kapısıyla oluşturan uzman ajandır.

## Hangi amaçla kullanılır`?

Müşterinin siparişle ilgili şikayetini almak (`orderId`, `complaintText`), siparişin geçerliliğini doğrulamak, onay kuyruğuna yazıp kullanıcıya pending bildirim dönmek ve gerektiğinde eskalasyon önerisi (`handoffSuggestion: "HumanHandoffAgent"`) sunmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/complaint-agent` sistem prompt'unu ve şikayet aracını bağlamak.
  - [SpecialistReasoningSchema](SpecialistReasoningSchema.md) ile ReAct çıktısı üretmek.
  - Breakpoint noktalarında (`OnBeforeRun`, `OnAfterRun`) şikayet araç çağrılarını izlemek.

## Constructor ve Başlatma Mantığı

```csharp
public ComplaintAgent(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate)
    : base(BuildInner(chatClient, prompts, approvalGate))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak `ApprovalGateService` üzerinden şikayet kayıt aracını bağlar ve `SupportAgentBase` temel sınıfına aktarır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate)
```
- **Ne işe yarar?:** Şikayet ajanının MAF `ChatClientAgent` örneğini yapılandırır.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.Complaint` ("ComplaintAgent") atanır.
  2. `Instructions`: `prompts.Get("agents/complaint-agent")` ile yüklenir.
  3. `Tools`: `approvalGate.BuildComplaintRegistrationTool()` bağlanır.
  4. `ResponseFormat`: `SpecialistReasoningSchema` camelCase JSON şeması atanır.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** LLM'e gidecek mesaj geçmişini ve kullanıcı şikayet detaylarını yakalar (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** Çağrılan araçları (`ToolCalls`) ve dönen onay/pending sonuçlarını (`ToolResults`) inceler (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- [ApprovalGateService](../ApprovalGateService.md)
- [SpecialistReasoningSchema](SpecialistReasoningSchema.md)
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
