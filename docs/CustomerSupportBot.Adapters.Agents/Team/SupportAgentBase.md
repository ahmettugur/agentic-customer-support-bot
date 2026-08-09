# SupportAgentBase

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/SupportAgentBase.cs`
**Erişim:** `internal abstract`
**Taban sınıf:** `Microsoft.Agents.AI.DelegatingAIAgent`

## Ne işe yarar?

Takımdaki 6 ajanın (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`) ortak iskeletidir: her ajan kendi prompt'unu/adını/tool listesini kendi sınıfında tanımlar, bu taban sınıf yalnızca **debug hook'larını** (`OnBeforeRun`/`OnAfterRun`) standartlaştırır.

## Hangi amaçla kullanılır?

Her `Team/*Agent.cs` sınıfı bu sınıftan türer ve `OnBeforeRun`/`OnAfterRun`'ı kendi ihtiyacına göre implemente eder (genelde breakpoint için — üretim kodunda bir iş yapmazlar, `_ = messages;` gibi no-op'turlar).

## Sorumlulukları

- LLM çağrısının **girdisini** (`OnBeforeRun` — tam mesaj listesi) ve **çıktısını** (`OnAfterRun` — tam `AgentResponse`) her ajanda ayrı ayrı yakalamak için ortak bir mekanizma sağlamak.
- Bu yakalamanın hem non-streaming (`RunCoreAsync`) hem streaming (`RunCoreStreamingAsync`) yolda **tutarlı** çalışmasını garanti etmek.
- Yardımcı statik metotlarla (`ToolCalls`, `ToolResults`) bir `AgentResponse`'tan tool çağrısı/sonucu içeriklerini çıkarmayı kolaylaştırmak.

**Üstlenmediği işler:** Prompt/tool/ad/açıklama tanımı (her alt sınıfın kendi `BuildInner` static metodunda) — bu taban sınıf tamamen davranışsal, veri taşımaz.

## Diğer katman ve bileşenlerle ilişkileri

**Taban sınıfı:** `Microsoft.Agents.AI.DelegatingAIAgent` — `ChatClientAgent` framework'te `sealed` olduğu için ondan doğrudan türetilemez; SDK'nın sunduğu bu delegating taban sınıf kullanılır (`Name`/`Id`/`Description` otomatik iç ajana yönlenir, davranış birebir korunur).

**Alt sınıflar:** `PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`.

**Kimler kullanır:** `AgentTeamFactory`, alt sınıfları örnekleyip `AIAgent` olarak workflow'a ekler.

## Kullanılma nedeni ve tasarım yaklaşımı

**Kritik tasarım detayı — neden hem `RunCoreAsync` hem `RunCoreStreamingAsync` override edilmeli:** `WorkflowRunner` her turda `TurnToken(emitEvents: true)` gönderir — bu, GroupChat'in `AIAgentHostExecutor`'ının ajanı **her zaman** streaming yoldan (`RunCoreStreamingAsync`) çağırması demektir, `RunCoreAsync`'ten değil. Sadece `RunCoreAsync` override edilseydi, debug hook'ları gerçek çalışan sistemde **hiç tetiklenmezdi** — `DelegatingAIAgent`'ın varsayılan `RunCoreStreamingAsync`'i doğrudan iç ajana geçer, sessizce. (Bu, projede bir süre fark edilmeden kalmış gerçek bir bug'dı — sonradan düzeltildi.)

`RunCoreStreamingAsync` override'ı, gelen `AgentResponseUpdate`'leri olduğu gibi `yield` edip ayrıca biriktirir; akış bitince biriken update'lerden `AgentResponseExtensions.ToAgentResponse(...)` ile `RunCoreAsync` ile aynı şekle sahip bir `AgentResponse` kurup `OnAfterRun`'a verir — böylece iki yoldaki debug hook'ları tutarlı davranır.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunCoreAsync(messages, session, options, ct)` (protected override) | Non-streaming yol: `OnBeforeRun` → `base.RunCoreAsync` → `OnAfterRun`. |
| `RunCoreStreamingAsync(messages, session, options, ct)` (protected override) | Streaming yol (gerçek çalışan sistemde kullanılan yol): `OnBeforeRun` → update'leri yield ederken biriktir → akış bitince `OnAfterRun(updates.ToAgentResponse())`. |
| `OnBeforeRun(messages)` (protected abstract) | Breakpoint noktası — LLM'e gönderilmek üzere olan tam mesaj listesi. |
| `OnAfterRun(response)` (protected abstract) | Breakpoint noktası — LLM'in bu ajan için ürettiği tam yanıt. |
| `ToolCalls(response)` (protected static) | `response.Messages`'tan `FunctionCallContent`'leri çıkarır. |
| `ToolResults(response)` (protected static) | `response.Messages`'tan `FunctionResultContent`'leri çıkarır. |

## Bağımlılıklar

Constructor: `SupportAgentBase(ChatClientAgent innerAgent)` — tek parametre, `DelegatingAIAgent` base constructor'ına geçirilir.
