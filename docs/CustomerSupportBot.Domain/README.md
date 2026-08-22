# CustomerSupportBot.Domain

Bu klasör, Onion / Hexagonal Mimarinin en iç çekirdeğini (**Core Domain**) oluşturan; hiçbir dış kütüphaneye, veritabanına, framework'e (MAF, EF Core, ASP.NET Core) bağımlı olmayan saf C# iş modellerini, alan kurallarını, ayrıştırıcı servisleri (Parsers) ve domain istisnalarını barındırır.

## Dizin Yapısı

- [Model/](../CustomerSupportBot.Adapters.Redis/README.md) — Temel domain varlıkları ve veri transfer modelleri:
  - `Customer`, `Product`, `Order`, `Complaint`, `HumanAgent` — E-ticaret domain varlıkları.
  - [AgentSession](Model/AgentSession.md) & `AgentSessionState` — Oturum durumu, doğrulanmış kimlik ve konuşma geçmişi.
  - [ReasoningResult](Model/ReasoningResult.md) & [PlanningResult](Model/PlanningResult.md) — 2 aşamalı niyet ve orkestrasyon planı modelleri.
  - [SpecialistReasoning](Model/SpecialistReasoning.md) — Uzman ajanların ReAct (PreToolCheck, Reflection) akıl yürütme şeması.
  - [ReasoningTrace](Model/ReasoningTrace.md) — Uçtan uca gözlemlenebilirlik ve trace ambarı modeli.
  - [Memory/](../CustomerSupportBot.Adapters.Redis/README.md) — Vektör belleği (`MemoryDocument`), müşteri profili (`CustomerUnderstanding`) modelleri.
  - [Auth/](../CustomerSupportBot.Adapters.Redis/README.md) — Kullanıcı kimliği (`UserInfo`), JWT (`RefreshTokenInfo`) modelleri.
  - `WellKnown` — Sistem genelinde paylaşılan sabitler (Ajan adları, Araç adları, Roller).
- [Services/](../CustomerSupportBot.Adapters.Redis/README.md) — Saf C# domain servisleri ve deterministik ayrıştırıcılar:
  - [IdExtractor](Services/IdExtractor.md) — Türkçe bağlam kelimeleriyle metinden deterministik olarak `order_id`, `customer_id`, `complaint_id` çıkaran servis.
  - [TokenEstimator](Services/TokenEstimator.md) — Metin uzunluğu ve kelime bazlı yaklaşık token hesaplayıcı.
  - [ReasoningResultParser](Services/ReasoningResultParser.md), [PlanningResultParser](Services/PlanningResultParser.md), [SpecialistReasoningParser](Services/SpecialistReasoningParser.md), [SelfCritiqueParser](Services/SelfCritiqueParser.md) — LLM çıktısı JSON bloklarını hata toleranslı ayrıştıran servisler.
  - [SessionStateExtractor](Services/SessionStateExtractor.md) — Oturum durumu güncelleyici.
- [Exceptions/](Exceptions/DomainException.md) — [DomainException](Exceptions/DomainException.md), `EntityNotFoundException`, `ValidationException`, `ExternalServiceException`, `ConcurrencyConflictException`.

## Mimari Kurallar ve Kısıtlar

1. **Sıfır Dış Bağımlılık:** Domain katmanı yalnızca standart .NET BCL kütüphanelerini (`System.*`, `System.Text.Json`, `System.Text.RegularExpressions`) kullanır.
2. **Deterministik ve Test Edilebilir:** Domain servisleri hiçbir I/O (veritabanı, ağ, LLM) çağrısı yapmaz; aynı girdi için her zaman aynı çıktıyı üretir.
