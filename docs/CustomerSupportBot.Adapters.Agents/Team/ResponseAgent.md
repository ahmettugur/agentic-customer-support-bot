# ResponseAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/ResponseAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.Response`
**Tool'ları:** Yok. **Structured output kullanmaz** — serbest metin üretir.

## Ne işe yarar?

Turun **son** ajanıdır: specialist'in (veya `PlanningAgent`'ın, netleştirme gerekiyorsa) yapılandırılmış çıktısını kullanıcıya sunulacak nihai, doğal dilde metne dönüştürür ve `"TERMINATE: reason=..."` işaretiyle workflow'u sonlandırır.

> 💡 **Analiz notu:** Haber spikeri gibi — muhabir (specialist agent) haberi ham veri olarak getirir, spiker (ResponseAgent) bunu güzel bir Türkçe ile izleyiciye sunar ve "haberler bitti" (TERMINATE) der.

## Hangi amaçla kullanılır?

`ReflectionRoutingStrategy`, her specialist mesajından sonra (eskalasyon/tamamlanma/bilinmeyen handoff durumlarında) bu ajana yönlenir. `PlanRoutingStrategy` da netleştirme gerektiğinde (`needsClarification=true` veya düşük confidence) doğrudan bu ajana yönlenir.

## Sorumlulukları

- Specialist'in ham çıktısını (tool sonuçları + `SpecialistReasoningSchema` JSON'u — `resultNotes`/`postToolReflection.summary`) okuyup kullanıcı diline (Türkçe, doğal) çevirmek.
- `"TERMINATE: reason=<sebep>"` işaretiyle mesajı bitirmek — `CustomerSupportChatManager.ShouldTerminateAsync` bu marker'ı arar.
- **Kullanıcıya akan gerçek zamanlı token'lar bu ajanın çıktısıdır** — `WorkflowRunner.ApplyTraceEvent`'teki `AgentResponseUpdateEvent` dalı yalnızca `ResponseAgent`'ın delta'larını `StreamEvent.ResponseDelta` olarak yayınlar; diğer ajanların (Planning/specialist) ham JSON çıktısı kullanıcıya asla akıtılmaz.

**Üstlenmediği işler:** Karar verme/routing (zaten kendisi turun sonu), tool çağrısı (tool'u yok).

## Diğer katman ve bileşenlerle ilişkileri

**Bağımlılıkları:** `IChatClient`, `IPromptRepository`.

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/response-agent.md`.

**Kimler tüketir:** `CustomerSupportChatManager.ShouldTerminateAsync` (`TERMINATE` marker'ını arar), `WorkflowRunner.ResponseStreamFilter` (marker'ı kullanıcıya akan metinden filtreler), `WorkflowResponseExtractor` (`ParseTerminationReasonFromResult`, `RemoveTerminationMarkers`).

## Kullanılma nedeni ve tasarım yaklaşımı

Bu ajan **bilinçli olarak** structured output kullanmaz — diğer 5 ajanın aksine ürettiği metin doğrudan kullanıcıya gider, bu yüzden serbest, doğal dilde kalmalıdır. `TERMINATE` marker'ının chunk sınırlarını bölebilme riski `WorkflowRunner.ResponseStreamFilter` tarafından ele alınır (marker uzunluğu kadar güvenlik payı tutularak).

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildInner(chatClient, prompts)` (private static) | `ChatClientAgent` kurar: tool yok, `ResponseFormat` yok. |
| `OnBeforeRun(messages)` | Breakpoint — specialist'in ürettiği ham çıktı (tool sonuçları + reasoning JSON) burada görülür. |
| `OnAfterRun(response)` | Breakpoint — `response.Text` (kullanıcıya gidecek nihai metin + sonundaki `TERMINATE` işareti). |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`.
