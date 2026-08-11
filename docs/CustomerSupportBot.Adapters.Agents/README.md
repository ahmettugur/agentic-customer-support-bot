# Adapters.Agents — Genel Bakış

`CustomerSupportBot.Adapters.Agents` projesi, uygulamanın **ajan katmanının** tüm orkestrasyon mantığını barındırır. Hexagonal mimaride bu proje bir **Driven Adapter**'dır: Application katmanının `IAgentTeamPort` ve `IEvaluationPort` portlarını, Microsoft Agent Framework (MAF) kullanarak implemente eder. Application katmanı kasıtlı olarak yalnızca Domain'e bağımlı kaldığı için (MAF referansı almaz), MAF tiplerine (`EvalItem`, `ApprovalRequiredAIFunction`, `ChatResponseFormat` vb.) dokunan HER ŞEY burada yaşar.

> 💡 **Analiz notu:** Bu katman projenin "beyni"dir. Application "bu soruyu çöz" der, bu adapter ise PlanningAgent → SpecialistAgent → ResponseAgent zincirini kurar, mesajları yönlendirir, tool çağrılarını yönetir ve sonucu döner. Bir orkestra şefinin notaları enstrümanlara dağıtması gibi — her agent bir enstrüman, bu katman onları koordine eden şeftir.

## Bu projeyi ne zaman açarsınız?

- Yeni bir ajan (agent) eklemek veya mevcut ajanın prompt/tool/structured-output davranışını değiştirmek istediğinizde → [Team/](Team/README.md)
- Workflow'un nasıl çalıştığını (mesaj hazırlığı, event işleme, HITL köprüsü, streaming) anlamak/değiştirmek istediğinizde → [WorkflowRunner](WorkflowRunner.md)
- Yönlendirme (routing) mantığını değiştirmeniz gerektiğinde → [Routing](Routing.md), [CustomerSupportChatManager](CustomerSupportChatManager.md)
- HITL (Human-in-the-Loop) onay kapısına yeni bir tool bağlamak istediğinizde → [ApprovalGateService](ApprovalGateService.md)
- Compound query (bileşik sorgu) orkestrasyonunu değiştirmek istediğinizde → [DecomposedRunner](DecomposedRunner.md)
- Tur-sonu yan etkilerini (eskalasyon, episodik bellek, müşteri profili) değiştirmek istediğinizde → [TurnFinalizer](TurnFinalizer.md)
- Evaluation senaryolarının nasıl çalıştırıldığını/değerlendirildiğini anlamak istediğinizde → [Evaluation/](Evaluation/README.md)

## Dosya haritası

```
CustomerSupportBot.Adapters.Agents/
│
├── CustomerSupportTeam.cs          # IAgentTeamPort implementasyonu — kompozisyon kökü (ince yönlendirici)
├── AgentTeamFactory.cs             # 6 ajanı örnekler, her koşu için taze Workflow üretir
├── WorkflowRunner.cs               # Gerçek orkestratör — tek sorgu koşusu, trace, HITL köprüsü, streaming
├── WorkflowMessageBuilder.cs       # Workflow'a giden system/user mesajlarının inşası
├── WorkflowTraceEventProcessor.cs  # Workflow event'lerini trace yan etkilerine çeviren işlemci
├── DecomposedRunner.cs             # Compound query orkestrasyonu (paralel/sıralı alt-görevler)
├── TurnFinalizer.cs                # Tur-sonu yan etkileri (eskalasyon, bellek, profil, trace kapatma)
├── CustomerSupportChatManager.cs   # MAF GroupChatManager — ajan seçimi ve sonlandırma
├── ApprovalGateService.cs          # HITL onay kapısı — yan etkili tool'lar buradan geçer
├── WorkflowResponseExtractor.cs    # MAF çıktısından anlamlı veri çıkarma yardımcısı
├── ExceptionTranslator.cs          # Framework exception → Domain exception dönüşümü
├── PortAliases.cs                  # Global using direktifleri (proje geneli namespace kısayolları)
│
├── Team/                            # Bkz. Team/README.md — 6 ajan + ortak taban + structured-output şeması
│   ├── SupportAgentBase.cs
│   ├── PlanningAgent.cs / ProductAgent.cs / OrderAgent.cs / ComplaintAgent.cs
│   ├── HumanHandoffAgent.cs / ResponseAgent.cs
│   └── SpecialistReasoningSchema.cs
│
├── Evaluation/                      # Bkz. Evaluation/README.md — IEvaluationPort implementasyonu
│   ├── EvaluationRunner.cs
│   └── CriteriaEvaluator.cs
│
├── Routing/
│   └── Routing.cs                  # Strategy pattern — 3 routing stratejisi + RoutingContext
│
└── DependencyInjection/
    └── AgentsAdapterServiceCollectionExtensions.cs  # DI kaydı (AddAgentsAdapter)
```

## Bileşenler arası ilişki

```
IChatPort (Application)
    │
    ▼
CustomerSupportTeam  ── implements IAgentTeamPort ── kompozisyon kökü
    │
    ├── new AgentTeamFactory(...)   ── 6 ajanı örnekler + CreateWorkflow()
    ├── new TurnFinalizer(...)      ── tur-sonu yan etkileri
    ├── new WorkflowRunner(factory, finalizer, ...)   ── GERÇEK orkestratör
    │       │
    │       ├─► AgentTeamFactory.CreateWorkflow()
    │       │       └─► CustomerSupportChatManager (her koşuda taze)
    │       │               ├── FirstTurnStrategy       ─┐
    │       │               ├── PlanRoutingStrategy       │ Routing/Routing.cs
    │       │               └── ReflectionRoutingStrategy─┘
    │       │
    │       ├─► HandleRequestInfoEventAsync()   ← ApprovalGateService (HITL köprüsü)
    │       ├─► EnsureHumanHandoffEscalation()  ← kod-seviyesi eskalasyon garantisi
    │       ├─► WorkflowResponseExtractor       ← sonuç temizleme
    │       └─► TurnFinalizer.FinalizeAsync()   ← turun sonu
    │
    └── new DecomposedRunner(_runner=WorkflowRunner, ...)  ── compound query → WorkflowRunner'ı N kez çağırır

IEvaluationPort (Application)
    │
    ▼
Evaluation/EvaluationRunner  ── IAgentTeamPort üzerinden CustomerSupportTeam'i (dolaylı) kullanır
    └── Evaluation/CriteriaEvaluator  ← MAF EvalCheck/EvalItem tipleri
```

## Detaylı dokümantasyon

| Dosya | Açıklama |
| --- | --- |
| [CustomerSupportTeam](CustomerSupportTeam.md) | Kompozisyon kökü — `IAgentTeamPort` implementasyonu, ince yönlendirici |
| [AgentTeamFactory](AgentTeamFactory.md) | 6 ajanı örnekler, taze `Workflow` üretir |
| [WorkflowRunner](WorkflowRunner.md) | Gerçek orkestratör — tek sorgu koşusu, trace, HITL köprüsü, streaming |
| [WorkflowMessageBuilder](WorkflowMessageBuilder.md) | Workflow'a giden system/user mesajlarının inşası |
| [WorkflowTraceEventProcessor](WorkflowTraceEventProcessor.md) | Workflow event'lerini trace yan etkilerine çeviren işlemci |
| [DecomposedRunner](DecomposedRunner.md) | Compound query orkestrasyonu |
| [TurnFinalizer](TurnFinalizer.md) | Tur-sonu yan etkileri |
| [CustomerSupportChatManager](CustomerSupportChatManager.md) | MAF GroupChatManager; ajan seçimi, sonlandırma korumaları |
| [ApprovalGateService](ApprovalGateService.md) | HITL onay kapısı; sipariş ve şikayet tool'ları |
| [Routing](Routing.md) | 3 routing stratejisi (Strategy pattern) |
| [WorkflowResponseExtractor](WorkflowResponseExtractor.md) | MAF output → temiz metin dönüşümü |
| [ExceptionTranslator](ExceptionTranslator.md) | Framework exception → Domain exception |
| [DependencyInjection](DependencyInjection.md) | DI kaydı ve ön koşullar |
| [Team/](Team/README.md) | 6 ajanın prompt/tool/schema tanımları |
| [Evaluation/](Evaluation/README.md) | `IEvaluationPort` implementasyonu — otomatik senaryo değerlendirme |

## Hızlı başlangıç: Yeni ajan eklemek

1. `CustomerSupportBot.Api/Prompts/agents/` altına `yeni-ajan.md` prompt dosyası oluşturun.
2. `Team/YeniAjan.cs` dosyasını oluşturun — `SupportAgentBase`'den türeyin, `BuildInner` static metodunda `ChatClientAgentOptions` ile `ChatClientAgent`'ı kurun (specialist ise `ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(...)` ekleyin, bkz. [Team/README.md](Team/README.md)).
3. `AgentTeamFactory` constructor'ında yeni ajanı örnekleyip `AIAgent` property olarak ekleyin.
4. `AgentTeamFactory.CreateWorkflow()`'daki `.AddParticipants(...)` çağrısına yeni ajanı ekleyin.
5. `WellKnown.AgentNames` sınıfına yeni ajan adını sabit olarak ekleyin — specialist ise `WellKnown.AgentNames.Specialists` dizisine de ekleyin (`Routing/Routing.cs`'deki `RoutingContext.IsSpecialistMessage`/`GetSpecialistName` bu diziyi kullanır).
