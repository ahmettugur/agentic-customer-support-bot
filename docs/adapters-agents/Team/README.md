# Team — Genel Bakış

`CustomerSupportBot.Adapters.Agents/Team/` klasörü, workflow'daki 6 ajanın (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`) prompt/tool/schema tanımlarını ve ortak iskeletlerini barındırır. `AgentTeamFactory` (bkz. [../AgentTeamFactory.md](../AgentTeamFactory.md)) bu sınıfları örnekleyip workflow'a katılımcı olarak ekler.

## Dosyalar

| Dosya | Açıklama |
|---|---|
| [SupportAgentBase](SupportAgentBase.md) | Tüm ajanların ortak taban sınıfı — debug hook'ları (`OnBeforeRun`/`OnAfterRun`) burada. |
| [PlanningAgent](PlanningAgent.md) | Müşteri talebini analiz eder, yapılandırılmış plan (JSON) üretir, uygun ajana yönlendirir. Tool'u yok. |
| [ProductAgent](ProductAgent.md) | Ürün sorgularını yanıtlar (tek ürün + katalog/kategori listeleme). Tüm tool'ları salt-okunur. |
| [OrderAgent](OrderAgent.md) | Sipariş oluşturma, sorgulama, iptal, iade. 3 tool'u HITL onayından geçer. |
| [ComplaintAgent](ComplaintAgent.md) | Şikayet kaydı. Tek tool'u HITL onayından geçer. |
| [HumanHandoffAgent](HumanHandoffAgent.md) | Kullanıcı açıkça insan temsilci istediğinde devreye girer, eskalasyon kaydı açar. |
| [ResponseAgent](ResponseAgent.md) | Turun son ajanı — specialist çıktısını nihai kullanıcı metnine çevirir, `TERMINATE` işaretiyle sonlandırır. |
| [SpecialistReasoningSchema](SpecialistReasoningSchema.md) | Specialist ajanların (Product/Order/Complaint/HumanHandoff) structured output şeması. |

## Ortak desen

```
SupportAgentBase (abstract, DelegatingAIAgent'tan türer)
    │
    ├── PlanningAgent      ─┐
    ├── ProductAgent        │  specialist'ler (WellKnown.AgentNames.Specialists):
    ├── OrderAgent          │  SpecialistReasoningSchema ile structured output kullanır
    ├── ComplaintAgent      │  (Product/Order/Complaint/HumanHandoff)
    ├── HumanHandoffAgent  ─┘
    └── ResponseAgent          (tool'u ve structured output'u yok — serbest metin üretir)
```

Her ajan kendi `BuildInner(...)` static metodunda bir `ChatClientAgent` kurar: `instructions` (prompt dosyasından), `name`, `description`, `tools` (varsa) ve — specialist'lerde — `ResponseFormat` (structured output). `SupportAgentBase`, `ChatClientAgent` `sealed` olduğu için ondan doğrudan türetilemediği için SDK'nın sunduğu `DelegatingAIAgent` taban sınıfını kullanır ve iki debug hook'u (`OnBeforeRun`/`OnAfterRun`) her ajanda ayrı ayrı implemente edilir.

## Prompt dosyaları

Her ajanın prompt'u `CustomerSupportBot.Api/Prompts/agents/<ajan-adı>.md` dosyasındadır (ör. `agents/order-agent.md`) ve `IPromptRepository.Get("agents/order-agent")` ile okunur — kod ve prompt içeriği ayrıdır, prompt değişikliği için kod değişikliği gerekmez.

## Structured Output (Specialist Ajanlar)

`PlanningAgent` (kendi `PlanningResult` şemasıyla) ve 4 specialist ajan (`SpecialistReasoningSchema` ile), `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(...)` kullanarak OpenAI/Azure OpenAI `response_format=json_schema` (strict) özelliğini devreye sokar — model artık markdown fence unutamaz veya alan atlayamaz. **Anthropic bridge'i bu ayarı okumuyor** (decompile ile doğrulandı) — o path'te sessizce no-op olur, bu yüzden `PlanningResultParser`/`SpecialistReasoningParser`'ın defensive fence-temizleme + alan-bazlı parse mantığı **kaldırılmadı**, tüm provider'larda çalışan tek güvence olarak kalıyor.

Specialist ajanların çıktısı **hiçbir zaman kullanıcıya doğrudan gösterilmez** — `CustomerSupportChatManager`'ın `ReflectionRoutingStrategy`'si (bkz. [../Routing.md](../Routing.md)) her specialist mesajından sonra her zaman `ResponseAgent`'a yönlenir (veya başka bir specialist'e dinamik handoff). Bu yüzden specialist'lerin çıktısını saf JSON'a zorlamak güvenlidir — kaybolan bir kullanıcı mesajı yoktur.
