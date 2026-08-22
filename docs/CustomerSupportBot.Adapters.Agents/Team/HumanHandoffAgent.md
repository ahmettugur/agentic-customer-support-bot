# HumanHandoffAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/HumanHandoffAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`HumanHandoffAgent`, kullanıcının açıkça bir canlı/insan müşteri temsilcisi talep ettiği durumlarda ("temsilciye bağla", "yetkili biriyle görüşmek istiyorum" vb.) devreye girerek somut bir iş yapmak yerine eskalasyon kaydı (`human_handoff_tool`) oluşturan ve kullanıcıya yönlendirme bilgilendirmesi yapan uzman ajandır.

## Hangi amaçla kullanılır`?

Canlı temsilci taleplerini resmi eskalasyon sürecine dönüştürmek, eskalasyon gerekçesini (`escalationReason`) belirlemek ve çıktıyı [SpecialistReasoningSchema](SpecialistReasoningSchema.md) ile yapılandırarak `ResponseAgent`'a aktarmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/human-handoff-agent` sistem prompt'unu ve `HumanHandoffTool` fonksiyonunu bağlamak.
  - [SpecialistReasoningSchema](SpecialistReasoningSchema.md) ile ReAct çıktısı üretmek.
  - Breakpoint noktalarında (`OnBeforeRun`, `OnAfterRun`) eskalasyon araç çağrılarını izlemek.

## Constructor ve Başlatma Mantığı

```csharp
public HumanHandoffAgent(IChatClient chatClient, IPromptRepository prompts)
    : base(BuildInner(chatClient, prompts))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak `HumanHandoffTool` fonksiyonuna sahip `ChatClientAgent` nesnesini yapılandırır ve `SupportAgentBase` temel sınıfına aktarır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
```
- **Ne işe yarar?:** Eskalasyon ajanının MAF `ChatClientAgent` örneğini yapılandırır.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.HumanHandoff` ("HumanHandoffAgent") atanır.
  2. `Instructions`: `prompts.Get("agents/human-handoff-agent")` ile yüklenir.
  3. `Tools`: `CustomerSupportToolsService.HumanHandoffTool` bağlanır.
  4. `ResponseFormat`: `SpecialistReasoningSchema` camelCase JSON şeması atanır.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** LLM'e gidecek mesajları yakalar (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** Çağrılan eskalasyon araçlarını (`ToolCalls`) ve üretilen yönlendirme metnini inceler (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- [SpecialistReasoningSchema](SpecialistReasoningSchema.md)
- `CustomerSupportBot.Application.Services.Tools.CustomerSupportToolsService`
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
