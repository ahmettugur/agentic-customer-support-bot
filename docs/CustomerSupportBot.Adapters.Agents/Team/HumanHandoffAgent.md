# HumanHandoffAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/HumanHandoffAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.HumanHandoff`
**Tool'ları:** `human_handoff_tool` (yan etkisiz — veri tabanına yazmaz)

## Ne işe yarar?

Kullanıcı açıkça insan/canlı temsilci istediğinde devreye girer ("temsilci bağla", "bottan sıkıldım" vb.). Somut bir iş yapmaz; eskalasyon kaydı açılmasını tetikler.

## Hangi amaçla kullanılır?

`PlanningAgent`, kullanıcının açıkça insan temsilci istediğini tespit ettiğinde (`selectedAgent="HumanHandoffAgent"`) bu ajana yönlendirir — bu kural diğer specialist seçimlerine göre **öncelikli**dir (sipariş/ürün/şikayet niyeti varsa bile kullanıcı doğrudan insan istiyorsa bu ajan seçilir). Intent tespiti `PlanningAgent`'ın kendi işi değildir (bkz. [PlanningAgent.md](PlanningAgent.md)) — bu karar, reasoning hint'indeki nihai intent + doğrudan kullanıcı ifadesi (`"temsilci bağla"` vb.) üzerinden verilir.

## Sorumlulukları

- `human_handoff_tool`'u çağırmak (`reason` parametresini kullanıcı mesajından çıkarır — zorunlu param eksikliği olmaz, `canProceed` her zaman `true`).
- `postToolReflection.status="needs_escalation"` üretmek — **zorunlu**, çünkü `EscalationPolicyService.ProcessPendingEscalations` yalnızca bu status'e bakarak eskalasyon açar.

**Üstlenmediği işler:** Eskalasyon kaydının gerçekten oluşturulması (`EscalationPolicyService`); LLM bu status'ü yanlış/eksik üretirse `WorkflowRunner.EnsureHumanHandoffEscalation` bunu **kod seviyesinde** düzeltir (bkz. [../WorkflowRunner.md](../WorkflowRunner.md)) — bu, ajanın kendisi değil, güvenlik ağı.

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`, `CustomerSupportToolsService.HumanHandoffTool` (statik metot, `Application.Services.Tools` namespace'inden).

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/human-handoff-agent.md`.

**Kimler tüketir:** `ReflectionRoutingStrategy`, `WorkflowRunner.EnsureHumanHandoffEscalation` (`WorkflowResponseExtractor.ContainsHumanHandoffToolCall` ile tool çağrısının kendisini deterministik sinyal olarak kullanır), `EscalationPolicyService`.

## Kullanılma nedeni ve tasarım yaklaşımı

**Structured output:** Diğer specialist'lerle aynı desen (bkz. [SpecialistReasoningSchema.md](SpecialistReasoningSchema.md)). Ama bu ajan için özellikle önemli: `status=needs_escalation` alanının **doğru format**ta üretilmesi (JSON şeması geçerliliği) garanti edilse bile, **doğru değer** taşıması garanti edilmez — bu yüzden `WorkflowRunner.EnsureHumanHandoffEscalation`'daki kod-seviyesi garanti hâlâ gereklidir (structured output format garantisi verir, değer doğruluğu garantisi vermez).

**Neden garanti tek yönlü:** `human_handoff_tool` çağrıldıysa (deterministik `FunctionCallContent` sinyali) LLM'in reflection'ı ne derse desin eskalasyon açılır — kaçırılan eskalasyonun maliyeti fazladan eskalasyondan yüksek görüldüğü ve admin panelinde dismiss yolu bulunduğu için.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `BuildInner(chatClient, prompts)` (private static) | `ChatClientAgent` kurar: tek tool + `ResponseFormat` = `SpecialistReasoningSchema`. |
| `OnBeforeRun(messages)` | Breakpoint — LLM'e gönderilen tam mesaj listesi. |
| `OnAfterRun(response)` | Breakpoint — `toolCalls` (`human_handoff_tool` hangi gerekçeyle çağrıldı), `response.Text`. |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`.
