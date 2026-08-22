# AgentTeamFactory

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/AgentTeamFactory.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`AgentTeamFactory`, Microsoft Agents Framework (MAF) üzerinde çalışan 6 uzman ajanı (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`) örnekleyen, her birine OpenTelemetry dağıtık izleme (tracing) yeteneği kazandıran ve her iş akışı koşusu için taze ve izole bir `AgentGroupWorkflow` inşa eden fabrika sınıfıdır.

## Hangi amaçla kullanılır`?

- **Performans ve Bellek Optimizasyonu:** Ajan örneklerini ve bunların araç bağlamlarını uygulama yaşam döngüsü boyunca bir kez oluşturup sabit tutarak gereksiz LLM istemcisi/araç fabrikası tahsisini engellemek.
- **Tur Bazlı İzolasyon:** Her workflow koşusu için taze bir [CustomerSupportChatManager](CustomerSupportChatManager.md) ve graph bağlantıları kurarak, handoff limitlerinin (`_handoffCounts`) ve döngü sayaçlarının (`IterationCount`) turlar arasında birbirini kirletmesini önlemek.
- **Gözlemlenebilirlik:** Her ajanın çağrısını `TelemetryConstants.ActivitySourceName` üzerinden OpenTelemetry span'leri ile sarmalayarak Jaeger, Aspire ve Application Insights gibi platformlara tam izleme verisi aktarmak.
- **Kısıtlı Uzman Akışları (Sub-Tasks):** Birleşik/karmaşık sorgularda belirli bir uzman ajana özel (`constrainedSpecialistName`) izole alt iş akışları oluşturmak.

## Sorumlulukları

- **Üstlendiği:**
  - 6 uzman ajanı kendi özelleştirilmiş prompt'ları, adları, açıklamaları ve araç kümeleriyle (`Team/` sınıfları) örneklemek.
  - Ajanlara OpenTelemetry ActivitySource sarmalaması (`WrapWithTelemetry`) eklemek.
  - `CreateWorkflow` çağrıldığında taze bir `CustomerSupportChatManager` ile `AgentWorkflowBuilder` üzerinden `Workflow` grafını derlemek.
  - Workflow seviyesinde maksimum yineleme (`MaximumIterationCount`) sınırını korumak.
- **Üstlenmediği:**
  - İş akışını yürütmek ve event loop'u işletmek (bu sorumluluk [WorkflowRunner](WorkflowRunner.md) sınıfındadır).
  - Kullanıcı mesajlarını veya RAG bağlamını hazırlamak (bu sorumluluk [WorkflowMessageBuilder](WorkflowMessageBuilder.md) sınıfındadır).

## Constructor ve Başlatma Mantığı

```csharp
public AgentTeamFactory(
    IChatClient chatClient,
    IPromptRepository prompts,
    ApprovalGateService approvalGate,
    ICustomerSupportToolsService tools,
    WorkflowGuardOptions guards,
    ILoggerFactory loggerFactory)
```

### Constructor İçerisinde Yapılan İşler:
1. **Bağımlılıkların Saklanması:** `_guards` (`WorkflowGuardOptions`) ve `_loggerFactory` alanları özel değişkenlere atanır.
2. **Telemetri Kaynak Adının Alınması:** `TelemetryConstants.ActivitySourceName` ("CustomerSupportBot") değeri okunur.
3. **6 Ajanın Örneklenmesi ve Telemetriyle Sarılması:**
   - **`PlanningAgent`**: `chatClient` ve `prompts` ile oluşturulur; `WrapWithTelemetry` ile sarılır.
   - **`ProductAgent`**: `chatClient`, `prompts` ve salt-okunur ürün araçları (`tools`) ile oluşturulur; `WrapWithTelemetry` ile sarılır.
   - **`OrderAgent`**: `chatClient`, `prompts` ve HITL onay kapılı sipariş araçları (`approvalGate`) ile oluşturulur; `WrapWithTelemetry` ile sarılır.
   - **`ComplaintAgent`**: `chatClient`, `prompts` ve HITL onay kapılı şikayet araçları (`approvalGate`) ile oluşturulur; `WrapWithTelemetry` ile sarılır.
   - **`HumanHandoffAgent`**: `chatClient` ve `prompts` ile oluşturulur; `WrapWithTelemetry` ile sarılır.
   - **`ResponseAgent`**: `chatClient` ve `prompts` ile oluşturulur; `WrapWithTelemetry` ile sarılır.

> **Tasarım Kararı:** Ajan nesneleri süreç ömrü boyunca tekil (singleton) olarak yaşar. Böylece araç kayıtları ve prompt referansları her istekte yeniden yüklenmez.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CreateWorkflow`
```csharp
public Workflow CreateWorkflow(string? constrainedSpecialistName = null)
```
- **Ne işe yarar?:** Microsoft Agents Framework üzerinden yeni bir grup sohbeti iş akışı (`Workflow`) nesnesi inşa eder.
- **İç Mantığı:**
  1. `AgentWorkflowBuilder.CreateGroupChatBuilderWith(...)` çağrısı başlatılır.
  2. Her çağrıda yeni bir [CustomerSupportChatManager](CustomerSupportChatManager.md) örneği oluşturulur. Bu yöneticiye 6 ajan listesi, güvenlik eşikleri (`_guards`), logger ve varsa kısıtlanmış uzman adı (`constrainedSpecialistName`) aktarılır.
  3. Yöneticinin `MaximumIterationCount` özelliğine `_guards.MaxIterations` (varsayılan: 15) atanır.
  4. `.AddParticipants(...)` metoduyla 6 uzman ajan (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`) grup sohbetinin katılımcıları olarak eklenir.
  5. `.Build()` çağrılarak çalıştırılmaya hazır `Workflow` grafı döndürülür.

### 2. `WrapWithTelemetry` (Private Static)
```csharp
private static AIAgent WrapWithTelemetry(AIAgent agent, string sourceName)
```
- **Ne işe yarar?:** Verilen bir `AIAgent` nesnesini Microsoft Agents AI telemetri middleware'i ile sarar.
- **İç Mantığı:** `agent.AsBuilder().UseOpenTelemetry(sourceName).Build()` zincirini koşturarak ajanın her çağrısında (LLM istekleri, token sayıları, gecikme süreleri) otomatik OpenTelemetry Activity span'i üretmesini sağlar.

## Özellikler (Properties)

| Özellik | Tür | Erişim | Açıklama |
|---|---|---|---|
| `PlanningAgent` | `AIAgent` | `public get;` | Kullanıcı talebini analiz edip yönlendirme planı (`PlanningResult`) üreten ajan örneği. |
| `ProductAgent` | `AIAgent` | `public get;` | Ürün arama, stok ve kategori sorgularını yürüten uzman ajan örneği. |
| `OrderAgent` | `AIAgent` | `public get;` | Sipariş sorgulama, sepet ve HITL onaylı sipariş işlemlerini yürüten uzman ajan örneği. |
| `ComplaintAgent` | `AIAgent` | `public get;` | Şikayet sorgulama ve HITL onaylı şikayet kayıt işlemlerini yürüten uzman ajan örneği. |
| `HumanHandoffAgent` | `AIAgent` | `public get;` | Canlı müşteri temsilcisine eskalasyon gereksinimini değerlendiren uzman ajan örneği. |
| `ResponseAgent` | `AIAgent` | `public get;` | Uzmanların ReAct çıktılarını müşteriye yönelik samimi bir Türkçe yanıta dönüştüren ve akışı sonlandıran ajan örneği. |

## Bağımlılıklar

- `Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder`
- `Microsoft.Agents.AI.Workflows.Workflow`
- `Microsoft.Extensions.AI.IChatClient`
- [CustomerSupportChatManager](CustomerSupportChatManager.md)
- [ApprovalGateService](ApprovalGateService.md)
- [WorkflowGuardOptions](../CustomerSupportBot.Application/Ports/Outbound/WorkflowGuardOptions.md)
- [IPromptRepository](../CustomerSupportBot.Application/Ports/Outbound/IPromptRepository.md)
- [ICustomerSupportToolsService](../CustomerSupportBot.Application/Ports/Outbound/ICustomerSupportToolsService.md)
- `CustomerSupportBot.Adapters.Agents.Team.*`
