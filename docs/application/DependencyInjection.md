# DependencyInjection

**Dosya:** `CustomerSupportBot.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`

## Giriş noktası

```csharp
services.AddApplicationDrivingPorts(configuration);
```

Bu tek çağrı, Application katmanındaki tüm servisleri DI container'a kaydeder. `Program.cs`'de veya `ApplicationServicesExtensions.cs`'de çağrılır.

## İç metodlar

`AddApplicationDrivingPorts` içeride 5 özel metot çağırır:

```
AddApplicationOptions(configuration)  ← appsettings bağlamaları
AddDrivingPorts()                      ← IChatPort, IReasoningPort, ... implementasyonları
AddChatServices()                      ← Tool, Reasoning, Chat servisleri
AddContextProviders()                  ← IContextProvider listesi
AddDomainServices()                    ← EntityVerifier, EvaluationRunner, ...
AddMemoryServices(configuration)       ← Semantic memory (koşullu)
```

---

## `AddApplicationOptions`

```csharp
services.AddOptions<ApprovalOptions>()
    .Bind(configuration.GetSection("HumanInTheLoop"))
    .Validate(o => o.TimeoutSeconds > 0, "HumanInTheLoop:TimeoutSeconds pozitif olmalı.")
    .Validate(o => o.ToolsRequiringApproval != null, "HumanInTheLoop:ToolsRequiringApproval null olamaz.")
    .ValidateOnStart();

services.Configure<RoutingOptions>(configuration.GetSection("Routing"));

services.AddOptions<ParallelExecutionOptions>()
    .Bind(configuration.GetSection(ParallelExecutionOptions.SectionName))
    .Validate(o => o.MaxDegreeOfParallelism > 0, "ParallelExecution:MaxDegreeOfParallelism pozitif olmalı.")
    .ValidateOnStart();

services.AddOptions<WorkflowGuardOptions>()
    .Bind(configuration.GetSection("WorkflowGuards"))
    .Validate(o => o.TimeoutSeconds > 0, "WorkflowGuards:TimeoutSeconds pozitif olmalı.")
    .Validate(o => o.MaxDuplicateToolCalls > 0, "WorkflowGuards:MaxDuplicateToolCalls pozitif olmalı.")
    .Validate(o => o.MaxIterations > 0, "WorkflowGuards:MaxIterations pozitif olmalı.")
    .Validate(o => o.MaxHandoffsPerAgent > 0, "WorkflowGuards:MaxHandoffsPerAgent pozitif olmalı.")
    .ValidateOnStart();

services.Configure<SlaOptions>(configuration.GetSection(SlaOptions.SectionName));
```

`appsettings.json` bölüm adları:

| Options sınıfı | appsettings bölümü | `ValidateOnStart` |
|---------------|-------------------|--------------------|
| `ApprovalOptions` | `HumanInTheLoop` | ✅ |
| `RoutingOptions` | `Routing` | — |
| `ParallelExecutionOptions` | `ParallelExecution` | ✅ |
| `WorkflowGuardOptions` | `WorkflowGuards` | ✅ |
| `SlaOptions` | `SLA` | — |

`ValidateOnStart()` işaretli üç options sınıfı için geçersiz bir değer (ör. `TimeoutSeconds=0`) artık ilk isteği değil **uygulama başlangıcını** patlatır — `IOptions<T>.Value`'nin lazy olarak ilk isteğe kadar okunmamasından kaynaklanan gizli config hatalarını önler.

---

## `AddDrivingPorts`

Tüm driving port implementasyonları **Singleton** olarak kaydedilir — `IChatPort`, `ISessionPort`, `IApprovalPort` ve diğerleri.

**İstisna:** `IRealtimeBridge` ve `IRealtimeNativeBridge` **Scoped** kaydedilir çünkü her realtime bağlantısı kendi durumunu taşır.

---

## `AddChatServices`

```csharp
services.AddSingleton<CustomerSupportToolsService>();
services.AddSingleton<ICustomerSupportToolsService>(sp =>
    sp.GetRequiredService<CustomerSupportToolsService>());

services.AddSingleton<ReasoningService>();
services.AddSingleton<IReasoningPort>(sp =>
    sp.GetRequiredService<ReasoningService>());

services.AddSingleton<ChatPortService>();
services.AddSingleton<IChatPort>(sp =>
    sp.GetRequiredService<ChatPortService>());

services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();
services.AddSingleton<IReplanService, ReplanService>();
services.AddSingleton<SessionStateService>();
```

`CustomerSupportToolsService`, `ReasoningService` ve `ChatPortService` hem concrete tip hem arayüz üzerinden erişilebilir. Concrete tipe ihtiyaç duyan bileşenler (test veya özel kullanım) doğrudan talep edebilir.

---

## `AddContextProviders`

```csharp
services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
services.AddSingleton<IContextProvider, CustomerProfileContextProvider>();
services.AddSingleton<IContextProvider, CustomerContextProvider>();
services.AddSingleton<ContextPipeline>();
services.AddSingleton<IContextPipeline>(sp =>
    sp.GetRequiredService<ContextPipeline>());
```

`IContextProvider`, DI tarafından `IEnumerable<IContextProvider>` olarak çözümlenir. `ContextPipeline` constructor'ı bu listeyi alır ve `Order`'a göre sıralar.

> `SemanticMemoryContextProvider`, `AddMemoryServices` içinde (semantic memory enabled ise) bu listeye eklenir.

---

## `AddDomainServices`

```csharp
services.AddSingleton<EntityVerifier>();
services.AddSingleton<ReasoningSanityChecker>();
services.AddSingleton<EvaluationRunner>();
services.AddSingleton<IEvaluationPort>(sp => sp.GetRequiredService<EvaluationRunner>());
services.AddSingleton<InputGuard>();
services.AddSingleton<IInputGuard>(sp => sp.GetRequiredService<InputGuard>());
services.AddSingleton<LessonMiner>();
services.AddSingleton<CustomerProfileService>();
services.AddSingleton<ICustomerProfileService>(sp => sp.GetRequiredService<CustomerProfileService>());
services.AddSingleton<ISkillsBasedRouter, SkillsBasedRouter>();
services.AddSingleton<EscalationPolicyService>();
```

---

## `AddMemoryServices` (koşullu)

`SemanticMemory.Enabled` değerine göre iki farklı kayıt seti uygulanır:

### Enabled = true

```csharp
services.AddSingleton<SemanticMemoryService>();
services.AddSingleton<ISemanticMemoryIngestor>(sp => sp.GetRequiredService<SemanticMemoryService>());
services.AddSingleton<ISemanticMemoryWriter>(sp => sp.GetRequiredService<SemanticMemoryService>());
services.AddSingleton<KnowledgeBaseIngestionService>();
services.AddSingleton<IKnowledgeBaseIngestor>(sp => sp.GetRequiredService<KnowledgeBaseIngestionService>());
services.AddSingleton<IMemoryPort, MemoryPortService>();
services.AddSingleton<IContextProvider, SemanticMemoryContextProvider>(); // ← Context pipeline'a eklenir
```

### Enabled = false

```csharp
services.AddSingleton<ISemanticMemoryIngestor, DisabledSemanticMemoryIngestor>(); // no-op
services.AddSingleton<IMemoryPort, DisabledMemoryPort>();
services.AddSingleton<IContextProvider, NoopContextProvider>();
// ISemanticMemoryWriter kayıtlı değil → null inject edilir
```

---

## Kayıt sırası önemli mi?

`AddApplicationDrivingPorts` her zaman aynı sırayla çağrılır. `AddAgentsAdapter()` (Adapters.Agents) ise **sonra** çağrılmalıdır çünkü `IChatClient`'ın zaten kayıtlı olmasını bekler.

Önerilen sıra (`Program.cs`'de):

```csharp
builder.Services.AddAiServices(configuration);           // 1. IChatClient
builder.Services.AddApplicationDrivingPorts(configuration); // 2. Application services
builder.Services.AddPersistenceAdapter(configuration);   // 3. ISessionManager, IOrderRepository...
builder.Services.AddAgentsAdapter();                     // 4. IAgentTeamPort (IChatClient gerektirir)
```

---

## Yeni servis eklemek

Uygun metoda singleton kaydı ekleyin:

```csharp
// AddDomainServices içine:
services.AddSingleton<YeniServis>();
services.AddSingleton<IYeniServisPort>(sp => sp.GetRequiredService<YeniServis>());
```

Port gerektirmiyorsa yalnızca concrete tip kaydı yeterlidir. Eğer yeni bir driven port ise bağımlı olduğu adapter'ın DI extension'ına da eklemeyi unutmayın.
