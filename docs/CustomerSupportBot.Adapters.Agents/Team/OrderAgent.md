# OrderAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/OrderAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.Order`
**Tool'ları:** `order_placement_tool` (HITL), `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`, `order_cancel_tool` (HITL), `return_request_tool` (HITL)

## Ne işe yarar?

Sipariş oluşturma, sorgulama, iptal ve iade işlemlerini yürütür. Salt-okunur tool'lar (`order_status`/`get_last_order`/`get_all_orders`) doğrudan çalışır; yan etkili olanlar (`placement`/`cancel`/`return`) `ApprovalGateService` HITL kapısından geçer.

> 💡 **Analiz notu:** E-ticaret sitesinin sipariş departmanı — "siparişim nerede?" sorularını yanıtlar, yeni sipariş oluşturur. Ama sipariş oluşturma/iptal gibi kritik işlemler için admin onayı gerekir.

## Hangi amaçla kullanılır?

`PlanningAgent` sipariş niyeti tespit ettiğinde (`selectedAgent="OrderAgent"`) veya bir başka specialist'in `postToolReflection.handoffSuggestion="OrderAgent"` demesiyle (`ReflectionRoutingStrategy`) devreye girer.

## Sorumlulukları

- 6 tool arasından doğruyu seçmek (`preToolCheck.selectedTool` — yalnızca bu ajanda var, birden fazla aday tool olduğu için model muhakemesini netleştiren bir alan; parser tarafından okunmaz).
- Zorunlu parametrelerin (ör. `customerId`, `orderId`, `reason`) toplanıp toplanmadığını denetlemek (`preToolCheck.canProceed`); eksikse **tek mesajda** hepsini istemek (ping-pong yok).
- Tool sonrası `postToolReflection` üretmek — sonucun durumu (`status`), tamamlanma bilgisi, olası dinamik handoff önerisi.
- HITL reddi durumunda (`"Tool call invocation rejected. {reason}"` düz metnini, JSON değil, tanıyıp `status="failed"` üretmek — bkz. aşağı).

**Üstlenmediği işler:** Onay bekleme mekaniği (`ApprovalGateService`), gerçek veri erişimi (`ICustomerSupportToolsService`).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`, `ApprovalGateService` (HITL-gated 3 tool için), `ICustomerSupportToolsService` (salt-okunur 3 tool için).

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/order-agent.md`.

**Kimler tüketir:** `ReflectionRoutingStrategy` (`postToolReflection`'a göre `ResponseAgent`'a veya başka bir specialist'e yönlendirir), `WorkflowRunner.ApplyTraceEvent`/`TurnFinalizer` (specialist reasoning'i trace'e ekler).

## Kullanılma nedeni ve tasarım yaklaşımı

**Structured output:** Çıktısı `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(...)` ile şemaya zorlanır (bkz. [SpecialistReasoningSchema.md](SpecialistReasoningSchema.md)). Bu güvenli çünkü `OrderAgent`'ın çıktısı **hiçbir zaman kullanıcıya doğrudan gitmez** — `ReflectionRoutingStrategy` her zaman `ResponseAgent`'a yönlenir. Prompt'taki eski "JSON'dan sonra kullanıcı mesajı yaz" talimatı bu yüzden kaldırıldı (kodun hiç okumadığı, artık strict şema altında zaten üretilemeyecek bir alan).

**Red-format kısıtı:** Admin bir HITL isteğini reddettiğinde `FunctionInvokingChatClient`, LLM'e JSON değil düz metin (`"Tool call invocation rejected. {reason}"`) döner — bu özelleştirilemez (bkz. [../ApprovalGateService.md](../ApprovalGateService.md)). `order-agent.md` promptuna bu formatı JSON gibi parse etmeye çalışmadan tanıyıp `status="failed"` üretecek özel bir talimat/tablo eklendi.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildInner(chatClient, prompts, approvalGate, tools)` (private static) | `ChatClientAgent` kurar: 6 tool + `ResponseFormat` = `SpecialistReasoningSchema`. |
| `OnBeforeRun(messages)` | Breakpoint — kullanıcı sorgusu, ENTITY EXTRACTION hint'i (`order_id` vb.), o ana kadarki grup sohbeti. |
| `OnAfterRun(response)` | Breakpoint — `toolCalls`, `toolResults` (not-found burada görülür), `response.Text`. |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`, `ApprovalGateService approvalGate`, `ICustomerSupportToolsService tools`.
