# CustomerSupportBot.Adapters.Agents

Bu klasör, hexagonal mimaride **Driven Adapter (Çıkış Adaptörü)** rolünü üstlenen ve Application katmanındaki [IAgentTeamPort](../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md) ile [IEvaluationPort](../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md) portlarını Microsoft Agents Framework (MAF) iş akışlarıyla (Workflows) somutlaştıran ajan adaptörünü barındırır.

## Dizin Yapısı

- [CustomerSupportTeam](CustomerSupportTeam.md) — `IAgentTeamPort` implementasyonu; tekil ve compound sorguları uygun koşucuya yönlendiren kompozisyon kökü.
- [AgentTeamFactory](AgentTeamFactory.md) — MAF `AgentGroupWorkflow` oluşturucu; 6 uzman ajanı örnekleyen ve OpenTelemetry ile saran fabrika.
- [CustomerSupportChatManager](CustomerSupportChatManager.md) — Çoklu ajan sohbetini yöneten, dinamik yönlendirme stratejilerini işleten ve sonlanma/tekrar kontrolü yapan `GroupChatManager`.
- [WorkflowRunner](WorkflowRunner.md) — Tek alt görev veya tekil sorguları MAF workflow'u üzerinden koşturan ve token akışını yöneten motor.
- [DecomposedRunner](DecomposedRunner.md) — Birleşik/karmaşık sorgulardaki alt görevleri (`SubTasks`) paralel veya sıralı gruplar halinde koordine eden koşucu.
- [IWorkflowRunner](IWorkflowRunner.md) — `WorkflowRunner` ile `DecomposedRunner` arasındaki soyut koşucu sözleşmesi.
- [ApprovalGateService](ApprovalGateService.md) — Yan etkili araç çağrılarını (sipariş, iptal, iade, şikayet) yakalayan ve HITL onay kapısını işleten güvenlik servisi.
- [WorkflowTraceEventProcessor](WorkflowTraceEventProcessor.md) — MAF iş akışı olaylarını dinleyip adım adım `ReasoningTrace` oluşturan işlemci.
- [WorkflowResponseExtractor](WorkflowResponseExtractor.md) — Ajanların yapılandırılmış `SpecialistReasoningSchema` JSON çıktılarını ve `ResponseAgent` metinlerini ayrıştıran bileşen.
- [WorkflowMessageBuilder](WorkflowMessageBuilder.md) — Context pipeline, kimlik ipuçları ve prompt şablonlarından workflow için başlangıç mesajlarını hazırlayan derleyici.
- [TurnFinalizer](TurnFinalizer.md) — Tur sonu yan etkilerini (trace tamamlama, onay temizleme, episodik hafıza ve müşteri profili güncelleme) yürüten sonlandırıcı.
- [TrimmingDeltaStreamer](TrimmingDeltaStreamer.md) — Son yanıttan önce sızan reasoning veya termination işaretçilerini filtreleyerek istemciye temiz SSE akışı sunan akış işleyici.
- [ExceptionTranslator](ExceptionTranslator.md) — MAF ve HTTP istisnalarını `ExternalServiceException` gibi domain istisnalarına çeviren yardımcı.
- [PortAliases](PortAliases.md) — Global namespace ve port import tanımları.
- [DependencyInjection](DependencyInjection/AgentsAdapterServiceCollectionExtensions.md) — `AddAgentsAdapter` DI kayıt uzantısı.
- [Team/](Team/README.md) — 6 uzman ajan (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`), temel sınıf `SupportAgentBase` ve `SpecialistReasoningSchema`.
- [A2A/](A2A/README.md) — Dış sistemlerle Agent-to-Agent protokolü üzerinden haberleşen salt-okunur ajan kataloğu (`A2AAgentCatalog`) ve `InputLimitedAgent`.
- [Routing](Routing.md) — Grup sohbeti yönlendirme stratejileri (`FirstTurnStrategy`, `PlanRoutingStrategy`, `ReflectionRoutingStrategy`).
- [Evaluation/](Evaluation/README.md) — YAML senaryolarını koşturan `EvaluationRunner` ve başarı kriterlerini denetleyen `CriteriaEvaluator`.

## Mimari Rolü ve Yetenekleri

- **Microsoft Agents Framework (MAF) Entegrasyonu:** `Microsoft.Agents.AI.Workflows` altyapısı ile deterministik, döngü korumalı ve gözlemlenebilir çoklu ajan takımı.
- **2 Aşamalı ReAct & JSON Şeması:** Uzman ajanlar serbest metin yerine `SpecialistReasoningSchema` şemasıyla yapılandırılmış akıl yürütme (`preToolCheck`, `postToolReflection`, güven skoru) üretir.
- **Decomposed Compound Query Desteği:** Birleşik kullanıcı isteklerini alt görevlere bölüp bağımsız olanları paralel, bağımlı olanları sıralı koşturma.
- **A2A Protokolü:** Dış sistemlere açık, yan etkisiz, salt-okunur ve girdi boyutu korumalı özel ajan arayüzü.
