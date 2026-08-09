# PlanningAgent

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/PlanningAgent.cs`
**Erişim:** `internal sealed`
**Taban sınıf:** [SupportAgentBase](SupportAgentBase.md)
**Ajan adı:** `WellKnown.AgentNames.Planning`
**Tool'ları:** Yok — yalnızca yönlendirme kararı verir.

## Ne işe yarar?

Müşteri talebini analiz eder, yapılandırılmış bir plan (JSON: `PlanningResult`) üretir ve uygun specialist ajana yönlendirir. Workflow'un **her turda ilk çalışan** ajanıdır (bkz. `Routing/Routing.cs` → `FirstTurnStrategy`).

## Hangi amaçla kullanılır?

`CustomerSupportChatManager.SelectNextAgentAsync`, sohbette hiç `PlanningAgent` mesajı yoksa (`FirstTurnStrategy`) her zaman bu ajanı seçer. Ürettiği `PlanningResult.SelectedAgent`, bir sonraki ajanı belirler (`PlanRoutingStrategy`).

## Sorumlulukları

- Kanıtları (`supportingEvidence`) toplamak. **Niyet (intent) tespiti yapmaz** — niyet ReasoningService'in tekil sorumluluğudur; reasoning hint'indeki intent nihai karar olarak kabul edilir.
- Uygun specialist ajanı seçmek (`selectedAgent`) ve gerekçesini (`rationale`) + reddedilen alternatifleri (`alternativesRejected`) üretmek.
- Netleştirme gerekip gerekmediğine (`needsClarification`) karar vermek — emin değilse `needsClarification=true` üretir; bu durumda `PlanRoutingStrategy` specialist yerine `ResponseAgent`'a yönlendirir.
- Seçilen ajana iletilecek görev tanımını (`taskDescription`) yazmak.
- Prompt injection / rol değiştirme girişimlerini kullanıcı niyeti olarak yorumlamak, sistem talimatlarını asla ifşa etmemek (`docs/`'taki "Talimat ayırımı" bölümü).

**Üstlenmediği işler:** Gerçek işlemi yapmak (sipariş/şikayet/ürün sorgusu) — yalnızca yönlendirir, tool'u yoktur.

## Diğer katman ve bileşenlerle ilişkileri

**Kimler tüketir:** `Routing/Routing.cs` → `PlanRoutingStrategy` (çıktısını `PlanningResultParser.TryParse` ile ayrıştırır, `SelectedAgent`'a göre yönlendirir); `WorkflowRunner.ApplyTraceEvent` (planı trace'e ekler).

**Prompt dosyası:** `CustomerSupportBot.Api/Prompts/agents/planning-agent.md`.

## Kullanılma nedeni ve tasarım yaklaşımı

**Structured output:** `PlanningAgent`'ın çıktısı `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlanningResult>(camelCaseOptions)` ile `PlanningResult` şemasına zorlanır (OpenAI/Azure OpenAI strict JSON schema). Bu, K3 analizinin "en yüksek kaldıraç" önerisiydi çünkü `PlanningAgent`'ın tool'u yok, saf JSON üretiyor — hiçbir prose/tool-call karışması riski taşımıyor, en düşük riskli ve en net kazançlı structured-output adayı. Prompt'taki eski "Bölüm 2 — Routing" satırı (`selectedAgent`'ı tekrar düz metin olarak yazma talimatı) koddan hiç okunmuyordu (`Routing.cs` yalnızca parse edilmiş `PlanningResult.SelectedAgent`'a bakıyor) — bu yüzden kaldırılması güvenliydi.

`PlanningResultParser`'ın fence-temizleme + alan-bazlı defensive parse mantığı **kaldırılmadı** — provider strict schema'yı honor etmediği durumda tek çalışan güvence bu parser'dır.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `BuildInner(chatClient, prompts)` (private static) | `ChatClientAgent` kurar: `Instructions` = `planning-agent.md`, `ResponseFormat` = `PlanningResult` şeması. |
| `OnBeforeRun(messages)` | Breakpoint — kullanıcı sorgusu, reasoning hint'i, entity extraction hint'i. |
| `OnAfterRun(response)` | Breakpoint — üretilen plan JSON'u (`response.Text`). |

## Bağımlılıklar

Constructor injection: `IChatClient chatClient`, `IPromptRepository prompts`.
