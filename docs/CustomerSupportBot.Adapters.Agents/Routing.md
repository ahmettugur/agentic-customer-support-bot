# Routing (Yönlendirme Stratejileri)

**Dosya:** `CustomerSupportBot.Adapters.Agents/Routing/Routing.cs`

## Genel Bakış

`CustomerSupportChatManager.SelectNextAgentAsync` metodu, hangi ajanın çalışacağına karar vermek için üç stratejiyi sırayla dener. Bunlar **Strategy pattern** ile tasarlanmıştır: her strateji bağımsız bir sınıftır, değerlendirmesini yapar ve eşleşmezse null döndürür.

> 💡 **Analiz notu:** Bir GPS navigasyon gibi — ilk rota (FirstTurnStrategy) kullanıcıyı PlanningAgent'a yönlendirir. Eğer PlanningAgent "OrderAgent'a git" derse ikinci rota (PlanRoutingStrategy) devreye girer. OrderAgent işini bitirince üçüncü rota (ReflectionRoutingStrategy) "ResponseAgent'a git" der.

```
SelectNextAgentAsync(history)
    │
    ├─ [1] FirstTurnStrategy.TrySelectAsync()         → eşleştiyse RoutingResult döner
    │      ↑ PlanningAgent daha önce konuşmadıysa
    │
    ├─ [2] PlanRoutingStrategy.TrySelectAsync()       → eşleştiyse RoutingResult döner
    │      ↑ Son mesaj PlanningAgent'tan geldiyse
    │
    ├─ [3] ReflectionRoutingStrategy.TrySelectAsync() → eşleştiyse RoutingResult döner
    │      ↑ Son mesaj bir Specialist'ten geldiyse
    │
    └─ (fallback) → PlanningAgent
```

## `IRoutingStrategy` arayüzü

```csharp
internal interface IRoutingStrategy
{
    ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? lastMessage,
        CancellationToken ct);
}
```

- **null döner** → "Bu strateji uygulanamaz, bir sonrakine geç."
- **`RoutingResult` döner** → "Bu ajanı seç, zinciri durdur."

## `RoutingResult`

```csharp
internal readonly record struct RoutingResult(AIAgent Agent, string Branch);
```

- `Agent`: Seçilen ajan
- `Branch`: Hangi stratejinin hangi koşuluyla seçildiği (log için). Örnek: `"plan"`, `"reflection_handoff"`

## `RoutingContext`

Tüm stratejiler paylaşılan bir `RoutingContext` ile oluşturulur. Bu nesne ajanlar sözlüğüne, guard ayarlarına ve logger'a erişim sağlar.

```csharp
internal sealed class RoutingContext
{
    public IReadOnlyDictionary<string, AIAgent> AgentsByName { get; init; }
    public AIAgent PlanningAgent { get; init; }
    public AIAgent ResponseAgent { get; init; }
    public WorkflowGuardOptions Guards { get; init; }
    public ILogger Logger { get; init; }

    public AIAgent? Resolve(string? name) // Ada göre ajan bulur
    public static bool IsSpecialistMessage(ChatMessage msg)  // Mesaj specialist'ten mi?
    public static string? GetSpecialistName(ChatMessage msg) // Hangi specialist?
}
```

**`IsSpecialistMessage`:** Mesajın `AuthorName`'i `WellKnown.AgentNames.Specialists` dizisindeki herhangi bir isimle başlıyorsa `true` döner. Yeni bir specialist ajan eklediğinizde bu diziye eklemeyi unutmayın.

> ⚠️ **Ad çakışması — `using` alias'ı:** `Microsoft.Extensions.AI` 10.9.0 kendi `RoutingContext` tipini ekledi. `CustomerSupportChatManager.cs` hem `Microsoft.Extensions.AI` hem `...Agents.Routing` namespace'lerini import ettiği için bu, CS0104 (belirsiz referans) derleme hatası verdi. Çözüm o dosyanın başındaki alias:
>
> ```csharp
> using RoutingContext = CustomerSupportBot.Adapters.Agents.Routing.RoutingContext;
> ```
>
> Buradaki `RoutingContext` **bizim** tipimizdir ve MEAI'nin aynı adlı tipiyle hiçbir ilgisi yoktur. MEAI sürümü yükseltilirken benzer çakışmalar çıkabilir; çözüm tipi yeniden adlandırmak değil, alias ile hangisinin kastedildiğini sabitlemektir.

## Strateji 1: `FirstTurnStrategy`

**Koşul:** Konuşma geçmişinde hiç PlanningAgent mesajı yok.

**Fikir:** Her konuşma PlanningAgent ile başlamalıdır. İlk turda bu strateji her zaman eşleşir.

```csharp
// Eğer history içinde PlanningAgent adına mesaj varsa → bu strateji uygulanamaz (null)
// Yoksa → PlanningAgent'ı döndür, branch = "first_turn"
```

**Branch:** `first_turn`

## Strateji 2: `PlanRoutingStrategy`

**Koşul:** Son mesaj PlanningAgent'tan gelmiş.

**Fikir:** PlanningAgent bir plan üretmiştir. Bu planı parse et ve hangi specialist ajana gidileceğini belirle.

```
PlanningAgent çıktısı
    │
    ▼
PlanningResultParser.TryParse(lastMessage.Text)
    │
    ├─► null          → Parse başarısız → ResponseAgent    branch: "plan_parse_failed"
    │
    ├─► NeedsClarification = true  → ResponseAgent         branch: "plan_clarification"
    │
    ├─► SelectedAgent bilinmiyor   → ResponseAgent         branch: "plan_unknown_agent"
    │
    └─► SelectedAgent biliniyor    → İlgili specialist ajan branch: "plan"
```

**Belirsizlik kuralı:** Clarification kararı artık bir güven skoru eşiğine değil, yalnızca planın `NeedsClarification` flag'ine bağlıdır — PlanningAgent emin olmadığında bu flag'i kendisi set eder.

## Strateji 3: `ReflectionRoutingStrategy`

**Koşul:** Son mesaj bir Specialist ajanından (OrderAgent, ComplaintAgent, ProductAgent, HumanHandoffAgent) gelmiş.

**Fikir:** Specialist ajan işini bitirdi ve `postToolReflection` alanında ne yapılacağını yazdı. Bu yansımayı oku ve sonraki adımı belirle.

```
Specialist çıktısı
    │
    ▼
SpecialistReasoningParser.TryParse(lastMessage.Text, agentName)
    │
    ├─► reflection = null         → ResponseAgent     branch: "reflection_missing"
    │
    ├─► NeedsEscalation           → ResponseAgent     branch: "reflection_escalation"
    │
    ├─► HandoffSuggestion var ve ResponseAgent değil
    │       └─► Hedef ajan biliniyorsa → Hedef ajan   branch: "reflection_handoff"
    │       └─► Bilinmiyorsa           → log + devam
    │
    └─► Diğer tüm durumlar        → ResponseAgent     branch: "reflection_complete"
```

**Handoff örneği:** OrderAgent şikayetin de işlenmesi gerektiğini anlayıp `"handoffSuggestion": "ComplaintAgent"` yazarsa, Strateji 3 bunu okuyup ComplaintAgent'ı seçer.

## `Branches` sabitleri

```csharp
internal static class Branches
{
    public const string FirstTurn           = "first_turn";
    public const string PlanParseFailed     = "plan_parse_failed";
    public const string PlanClarification   = "plan_clarification";
    public const string PlanUnknownAgent    = "plan_unknown_agent";
    public const string Plan                = "plan";
    public const string ReflectionMissing   = "reflection_missing";
    public const string ReflectionEscalation = "reflection_escalation";
    public const string ReflectionHandoff   = "reflection_handoff";
    public const string ReflectionComplete  = "reflection_complete";
}
```

Bu sabitler log mesajlarında `"Agent selected via {Branch}: {Agent}"` formatında kullanılır. Log'larda hangi stratejinin seçildiğini anlamak için bu değerleri arayın.

## Yeni strateji eklemek

1. `Routing.cs` dosyasında `IRoutingStrategy`'yi implemente eden yeni bir `internal sealed class` oluşturun.
2. `CustomerSupportChatManager` constructor'ında `_strategies` listesine ekleyin:

   ```csharp
   _strategies =
   [
       new FirstTurnStrategy(ctx),
       new PlanRoutingStrategy(ctx),
       new YeniStrateji(ctx),        // ← ekleyin
       new ReflectionRoutingStrategy(ctx),
   ];
   ```

3. Sıra önemlidir: daha spesifik stratejiler üste, daha genel olanlar alta gelmelidir.

## Tam örnek akış

```
Kullanıcı: "4821 nerede?"

Turn 1:
  history = [System, System, User]
  → FirstTurnStrategy: PlanningAgent'tan mesaj yok → PlanningAgent seçildi [first_turn]

PlanningAgent yanıtı:
  { "selected_agent": "OrderAgent", "needs_clarification": false }

Turn 2:
  lastMessage.AuthorName = "PlanningAgent"
  → PlanRoutingStrategy: parse başarılı, needsClarification=false, agent=OrderAgent
  → OrderAgent seçildi [plan]

OrderAgent yanıtı:
  { "postToolReflection": { "status": "completed", "handoffSuggestion": "ResponseAgent" } }

Turn 3:
  lastMessage.AuthorName = "OrderAgent" (Specialist)
  → ReflectionRoutingStrategy: handoffSuggestion="ResponseAgent" → ResponseAgent seçildi [reflection_complete]

ResponseAgent yanıtı:
  "4821 numaralı siparişiniz kargoda. TERMINATE"

ShouldTerminateAsync → true → Workflow biter
```
