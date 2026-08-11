# ProductAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/ProductAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.Product`
**Tool'ları:** `product_inquiry_tool`, `product_list_tool` (ikisi de salt-okunur)

## Ne işe yarar?

Ürün sorgularını yanıtlar — tek ürün sorgulama ve katalog/kategori bazlı listeleme. Tüm tool'ları salt-okunur olduğu için compound query'lerde diğer read-only specialist'lerle paralel çalıştırılabilir (bkz. `WellKnown.AgentNames.ReadOnly`, `ParallelExecutionOptions.IsReadOnly`, [../DecomposedRunner.md](../DecomposedRunner.md)).

> 💡 **Analiz notu:** Mağazadaki ürün danışmanı gibi — "bu ürün ne kadar?", "elektronik kategorisinde ne var?" sorularını yanıtlar. Stok/fiyat bilgisi verir ama sipariş almaz.

## Hangi amaçla kullanılır?

`PlanningAgent` ürün sorgusu/listesi niyeti tespit ettiğinde devreye girer.

## Sorumlulukları

- Uygun tool'u (tek ürün vs. liste/kategori) seçip çağırmak.
- Tool sonrası `postToolReflection` üretmek — ör. ürün bulunamadıysa `status="partial"`, `resultConfidence=0.4`.
- Sonuç UI ipuçları (ör. `category_picker`) üretebilecek şekilde tool sonucunu döndürmek (`IUiHintEmitter` tool implementasyon tarafında devreye girer).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`, `ICustomerSupportToolsService` (her iki tool da salt-okunur, HITL gerektirmez).

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/product-agent.md`.

**Kimler tüketir:** `ReflectionRoutingStrategy`, `WorkflowRunner.ApplyTraceEvent`/`TurnFinalizer`.

## Kullanılma nedeni ve tasarım yaklaşımı

**Structured output:** [OrderAgent](OrderAgent.md)/[ComplaintAgent](ComplaintAgent.md) ile aynı desen — `SpecialistReasoningSchema` (bkz. [SpecialistReasoningSchema.md](SpecialistReasoningSchema.md)). Tool'ları salt-okunur olduğu için HITL red-format kısıtı bu ajanı etkilemez.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildInner(chatClient, prompts, tools)` (private static) | `ChatClientAgent` kurar: 2 tool + `ResponseFormat` = `SpecialistReasoningSchema`. |
| `OnBeforeRun(messages)` | Breakpoint — LLM'e gönderilen tam mesaj listesi. |
| `OnAfterRun(response)` | Breakpoint — `toolCalls`/`toolResults` (UI hint'leri ör. `category_picker` bu sonuçtan üretilir), `response.Text`. |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`, `ICustomerSupportToolsService tools`.
