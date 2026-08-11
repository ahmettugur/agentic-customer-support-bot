# CustomerSupportChatManager

**Dosya:** `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs`  
**Base class:** `GroupChatManager` (Microsoft.Agents.AI.Workflows)  
**Yaşam döngüsü:** Her `CreateWorkflow()` çağrısında yeni instance oluşturulur

## Ne yapar?

MAF (Microsoft Agents Framework), bir workflow içinde birden fazla ajanı "grup sohbet" modunda çalıştırır. Bu modda **kim konuşacak?** sorusunu cevaplamak `GroupChatManager`'ın görevidir. `CustomerSupportChatManager` bu soruyu iki farklı hook ile yanıtlar:

> 💡 **Analiz notu:** Bir toplantı yöneticisi gibi — "şimdi sıra muhasebede (OrderAgent), sonra hukuk kontrolü (ResponseAgent)" diye söz hakkı verir. Toplantı sonlanma koşullarını da kontrol eder.

1. **`SelectNextAgentAsync`** — Her turda hangi ajan çalışmalı?
2. **`ShouldTerminateAsync`** — Konuşma bitmeli mi?

## Ajan seçimi: 3-Strategy zinciri

Ajan seçimi bir **Chain of Responsibility** (Strategy zinciri) ile yapılır. Stratejiler sırayla denenir; ilki uygun ajanı bulursa zincir durur.

```
SelectNextAgentAsync(history)
    │
    ├─► [1] FirstTurnStrategy       → PlanningAgent ile başla
    │
    ├─► [2] PlanRoutingStrategy     → PlanningAgent çıktısını parse et, ilgili ajana git
    │
    └─► [3] ReflectionRoutingStrategy → Specialist ajanın yansımasını oku, sonraki adımı seç
            │
            └─► (hiçbiri uygun değilse) → PlanningAgent fallback
```

Detaylar için bkz. [Routing.md](Routing.md).

## Handoff limit koruması: `EnforceHandoffLimit`

Bir ajan sonsuz döngüye girmesini engellemek için her ajan için handoff sayacı tutulur:

```csharp
private readonly Dictionary<string, int> _handoffCounts;
```

Bir ajana `WorkflowGuardOptions.MaxHandoffsPerAgent` kez handoff yapıldıktan sonra, o ajana olan sonraki yönlendirme `ResponseAgent`'a çevrilir. Branch adına `+handoff_limited` eklenir ve log'a kaydedilir.

> **Not:** PlanningAgent ve ResponseAgent bu limitten muaftır — bunlara sınırsız geçiş yapılabilir.

## Sonlandırma: `ShouldTerminateAsync`

Workflow'u sonlandıran iki koşul vardır:

### 1. TERMINATE marker

Herhangi bir mesaj `WellKnown.Termination.Marker` stringini içeriyorsa (ResponseAgent bu marker'ı bilinçli olarak yazar) workflow durur.

```
"Siparişiniz oluşturuldu. TERMINATE reason=completed"
                          ^^^^^^^^^^^
                          Bu görülünce workflow sona erer
```

### 2. Tekrarlayan tool çağrısı tespiti: `DetectRepeatedToolCall`

Son 10 mesaj içinde aynı tool, aynı parametrelerle `WorkflowGuardOptions.MaxDuplicateToolCalls` kez çağrıldıysa workflow sonlandırılır. Bu, ajanın bir döngüye girip gereksiz API çağrıları yapmasını engeller.

**Nasıl çalışır?**

```csharp
// Tool imzası: "tool_name:{"param1":"val1","param2":"val2"}"
// Parametreler alfabetik sırayla serialize edilir → deterministik karşılaştırma
private static string BuildToolSignature(string toolName, IDictionary<string, object?>? args)
```

Tool'ların parametreleri `OrderBy(kv => kv.Key)` ile sıralandıktan sonra JSON olarak serialize edilir. Bu sayede parametre sırası farklı olsa da aynı çağrı olduğu doğru tespit edilir.

## Constructor parametreleri

```csharp
public CustomerSupportChatManager(
    IReadOnlyList<AIAgent> agents,        // Workflow'daki tüm ajanlar
    WorkflowGuardOptions guards,          // Koruma eşikleri
    ILogger<CustomerSupportChatManager> logger)
```

Constructor içinde `PlanningAgent` ve `ResponseAgent`'ın listede mevcut olduğu doğrulanır. Eksikse `InvalidOperationException` fırlatılır.

## WorkflowGuardOptions referansı

Bu sınıf tarafından kullanılan ayarlar (`appsettings.json` → `Workflow:` bölümü):

| Ayar | Tür | Kullanım yeri |
| ------ | ----- | --------------- |
| `MaxIterations` | int | `GroupChatManager.MaximumIterationCount` |
| `MaxHandoffsPerAgent` | int | `EnforceHandoffLimit` |
| `MaxDuplicateToolCalls` | int | `DetectRepeatedToolCall` |
| `TimeoutSeconds` | int | `CustomerSupportTeam` (burada kullanılmaz) |

## Örnek akış

```
Turn 1: SelectNextAgent → FirstTurnStrategy → PlanningAgent
        PlanningAgent: { "selected_agent": "OrderAgent", "intent": "order_inquiry" }

Turn 2: SelectNextAgent → PlanRoutingStrategy → OrderAgent
        OrderAgent: { "preToolCheck": {...}, "postToolReflection": { "handoffSuggestion": "ResponseAgent" } }

Turn 3: SelectNextAgent → ReflectionRoutingStrategy → ResponseAgent
        ResponseAgent: "Siparişiniz 3. kargo gününde teslim edilecek. TERMINATE"

ShouldTerminate → true (TERMINATE bulundu) → Workflow biter
```

## Sık karşılaşılan durumlar

### "No routing strategy matched; falling back to PlanningAgent"

Log'da bu uyarıyı görüyorsanız, 3 stratejinin hiçbiri eşleşmedi demektir. Olası nedenler:

- `lastMessage` null
- Mesajın `AuthorName`'i beklenen ajan adıyla eşleşmiyor
- `RoutingContext.IsSpecialistMessage` false döndürüyor (yeni ajan eklendiyse `WellKnown.AgentNames.Specialists` dizisini kontrol edin)

### Handoff limit tetikleniyor ama beklenmiyordu

`WorkflowGuardOptions.MaxHandoffsPerAgent` değerini artırın veya ajanın neden tekrar tekrar seçildiğini araştırın — genellikle tool başarısız olduğunda ajan tekrar çağrılmaya çalışılır.
