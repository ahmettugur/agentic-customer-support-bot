# ComplaintAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/ComplaintAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.Complaint`
**Tool'ları:** `complaint_registration_tool` (HITL)

## Ne işe yarar?

Müşteri şikayetlerini kaydeder. Tek tool'u yan etkilidir ve `ApprovalGateService` HITL kapısından geçer (admin onayı beklenir).

> 💡 **Analiz notu:** Müşteri hizmetleri şikayet birimi — "ürünüm bozuk geldi" dediğinde şikayeti kayıt altına alır. Ama kayıt admin onayına tabidir çünkü şikayet açmak geri dönüşü olmayan bir işlemdir.

## Hangi amaçla kullanılır?

`PlanningAgent` şikayet niyeti tespit ettiğinde (`selectedAgent="ComplaintAgent"`) veya bir başka specialist'in dinamik handoff önerisiyle devreye girer.

## Sorumlulukları

- Zorunlu parametrelerin (`orderId`, `description`) toplanıp toplanmadığını denetlemek — `customerId` opsiyoneldir, tool otomatik türetir, `missingParams`'a sayılmaz.
- Eksik zorunlu alan varsa **tek mesajda** hepsini istemek (ping-pong yok).
- Tool sonrası `postToolReflection` üretmek.
- HITL reddi durumunda düz metin red formatını tanıyıp `status="failed"` üretmek.

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`, `ApprovalGateService` (tek tool HITL-gated).

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/complaint-agent.md`.

**Kimler tüketir:** `ReflectionRoutingStrategy`, `WorkflowRunner.ApplyTraceEvent`/`TurnFinalizer`.

## Kullanılma nedeni ve tasarım yaklaşımı

**Structured output:** [OrderAgent](OrderAgent.md) ile aynı desen — `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(...)` (bkz. [SpecialistReasoningSchema.md](SpecialistReasoningSchema.md)). Çıktısı kullanıcıya doğrudan gitmediği için (`ReflectionRoutingStrategy` her zaman `ResponseAgent`'a yönlenir) güvenli. Eski "JSON'dan sonra kullanıcı mesajı yaz" talimatı kaldırıldı.

**Red-format kısıtı:** [OrderAgent.md](OrderAgent.md)'deki ile aynı — `complaint-agent.md` promptuna `"Tool call invocation rejected. {reason}"` düz metnini tanıyacak özel talimat eklendi.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildInner(chatClient, prompts, approvalGate)` (private static) | `ChatClientAgent` kurar: tek tool + `ResponseFormat` = `SpecialistReasoningSchema`. |
| `OnBeforeRun(messages)` | Breakpoint — LLM'e gönderilen tam mesaj listesi. |
| `OnAfterRun(response)` | Breakpoint — `toolCalls` (`orderId`, `complaintText`, `customerId`), `toolResults` (onay reddedildiyse `ValidationError` burada görülür). |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`, `ApprovalGateService approvalGate`.
