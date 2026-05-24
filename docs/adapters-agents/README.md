# Adapters.Agents — Genel Bakış

`CustomerSupportBot.Adapters.Agents` projesi, uygulamanın **ajan katmanının** tüm orkestrasyon mantığını barındırır. Hexagonal mimaride bu proje bir **Driven Adapter** olarak konumlanır: Application katmanının `IAgentTeamPort` portunu, Microsoft Agents Framework (MAF) kullanarak implemente eder.

## Bu projeyi ne zaman açarsınız?

- Yeni bir ajan (agent) eklemek veya mevcut ajanın davranışını değiştirmek istediğinizde
- Yönlendirme (routing) mantığını değiştirmeniz gerektiğinde
- HITL (Human-in-the-Loop) onay kapısına yeni bir tool bağlamak istediğinizde
- Workflow sonucunun temizlenmesi / işlenmesi ile ilgili bir değişiklik yapmanız gerektiğinde

## Dosya haritası

```
CustomerSupportBot.Adapters.Agents/
│
├── CustomerSupportTeam.cs          # Ana orkestratör — IAgentTeamPort implementasyonu
├── CustomerSupportChatManager.cs   # MAF GroupChatManager — ajan seçimi ve sonlandırma
├── ApprovalGateService.cs          # HITL onay kapısı — yan etkili tool'lar buradan geçer
├── WorkflowResponseExtractor.cs    # MAF çıktısından anlamlı veri çıkarma yardımcısı
├── ExceptionTranslator.cs          # Framework exception → Domain exception dönüşümü
├── PortAliases.cs                  # Global using alias'lar
│
├── Routing/
│   └── Routing.cs                  # Strategy pattern — 3 routing stratejisi + RoutingContext
│
└── DependencyInjection/
    └── AgentsAdapterServiceCollectionExtensions.cs  # DI kaydı
```

## Bileşenler arası ilişki

```
IChatPort (Application)
    │
    ▼
CustomerSupportTeam  ──────────────────────────────────────────────────────────┐
    │  implements IAgentTeamPort                                                 │
    │                                                                            │
    ├── CreateWorkflow()                                                         │
    │       └─► AgentWorkflowBuilder + CustomerSupportChatManager               │
    │                   │                                                        │
    │                   └─► [FirstTurnStrategy]                                  │
    │                       [PlanRoutingStrategy]      ← Routing/Routing.cs      │
    │                       [ReflectionRoutingStrategy]                          │
    │                                                                            │
    ├── BuildWorkflowMessagesAsync()   ← ContextPipeline + ReasoningHint        │
    │                                                                            │
    ├── RewriteRoutingMessageAsync()   ← routing-rewrite-* prompt'ları          │
    │                                                                            │
    ├── RunDecomposedAsync()           ← SubTaskOrchestrator (compound query)   │
    │                                                                            │
    └── WorkflowResponseExtractor     ← WorkflowResponseExtractor.cs            │
            ├── ExtractResultFromOutput                                          │
            ├── RemoveTerminationMarkers                                         │
            └── RemoveTechnicalJsonBlocks                                        │
                                                                                 │
ApprovalGateService  ───────────────────────────────────────────────────────────┘
    ├── BuildOrderPlacementTool()       ← HITL gate + IApprovalQueue
    ├── BuildComplaintRegistrationTool() ← HITL gate + IApprovalQueue
    └── ProcessPendingEscalations()
```

## Detaylı dokümantasyon

| Dosya | Açıklama |
|---|---|
| [CustomerSupportTeam](CustomerSupportTeam.md) | Ana orkestratör; 6 ajan, workflow, streaming, compound query |
| [CustomerSupportChatManager](CustomerSupportChatManager.md) | MAF GroupChatManager; ajan seçimi, sonlandırma korumaları |
| [ApprovalGateService](ApprovalGateService.md) | HITL onay kapısı; sipariş ve şikayet tool'ları |
| [Routing](Routing.md) | 3 routing stratejisi (Strategy pattern) |
| [WorkflowResponseExtractor](WorkflowResponseExtractor.md) | MAF output → temiz metin dönüşümü |
| [ExceptionTranslator](ExceptionTranslator.md) | Framework exception → Domain exception |
| [DependencyInjection](DependencyInjection.md) | DI kaydı ve ön koşullar |

## Hızlı başlangıç: Yeni ajan eklemek

1. `CustomerSupportBot.Api/Prompts/agents/` altına `yeni-ajan.md` prompt dosyası oluşturun.
2. `CustomerSupportTeam.cs` içinde `_yeniAjan` field'ı tanımlayın ve constructor'da başlatın:
   ```csharp
   _yeniAjan = WrapWithTelemetry(new ChatClientAgent(
       chatClient,
       instructions: _prompts.Get("agents/yeni-ajan"),
       name: WellKnown.AgentNames.YeniAjan,
       description: "..."), sourceName);
   ```
3. `CreateWorkflow()` içinde `.AddParticipants(...)` çağrısına yeni ajanı ekleyin.
4. `WellKnown.AgentNames` sınıfına yeni ajan adını sabiti olarak ekleyin.
5. `Routing/Routing.cs` içindeki `RoutingContext.IsSpecialistMessage` ve `RoutingContext.GetSpecialistName` metodlarının `WellKnown.AgentNames.Specialists` dizisini kullandığını unutmayın — yeni ajanı bu diziye de eklemeniz gerekir.
