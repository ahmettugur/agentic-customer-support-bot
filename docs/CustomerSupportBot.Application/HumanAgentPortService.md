# HumanAgentPortService

**Dosya:** `Services/Escalation/HumanAgentPortService.cs`  
**Implements:** `IHumanAgentPort`, `IDisposable`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Human agent (temsilci) kaydını ve yük takibini yönetir. Temsilcilerin sisteme kaydolması, yük sayaçlarının güncellenmesi ve eskalasyon yeniden yönlendirmesi bu servis üzerinden yapılır.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IHumanAgentRegistry` | Temsilci kayıt deposu |
| `IEscalationSink` | Eskalasyon state erişimi |
| `ILogger<HumanAgentPortService>` | Loglama |

**Constructor'da:** yalnızca `IEscalationSink.RequestDecided` event'ine `_loadTrackingHandler` abone olunur (ayrı bir `RequestDismissed` event'i **yoktur** — dismiss durumu da `RequestDecided` üzerinden gelir). Handler, eskalasyon `Resolved` veya `Dismissed` olduğunda ilgili `SuggestedAgentId`'nin yükünü otomatik azaltır.

---

## Metodlar

Sadece `GetAllMergedAsync` gerçekten async'tir; geri kalan tüm metodlar **senkrondur**.

### `GetAllMergedAsync`

```csharp
Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default)
```

`IHumanAgentRegistry.GetAll()` (kayıtlı temsilciler) ile `GetLinkedUsersAsync` (auth kullanıcılarından türetilen, kayıtlı olmayanlar) birleştirilir. Sonuç `DisplayName`'e göre sıralanır.

---

### `GetAgent / CreateAgent / UpdateAgent / DeleteAgent`

```csharp
HumanAgent? GetAgent(string id)
HumanAgent CreateAgent(HumanAgent agent)
HumanAgent? UpdateAgent(string id, HumanAgentInput input)
bool DeleteAgent(string id)
```

Senkron CRUD — `IHumanAgentRegistry` metodlarına doğrudan delege eder.

---

### `IncrementLoad / DecrementLoad`

```csharp
bool IncrementLoad(string id)
bool DecrementLoad(string id)
```

Senkron. Temsilcinin `CurrentLoad` sayacını artırır/azaltır.

> **Not:** `ChatSessionPortService.TakeOver`/`Release` bu metodları **doğrudan `IHumanAgentRegistry` üzerinden** çağırır — `HumanAgentPortService` üzerinden değil.

---

### `RerouteEscalation`

```csharp
RerouteResult RerouteEscalation(string escalationId, string? agentId, string? reason)
```

`public sealed record RerouteResult(EscalationRequest? Updated, string? Error);`

Bir eskalasyonu eski temsilciden yeni temsilciye (veya atamayı kaldırmak için `agentId = null`) aktarır. `IEscalationSink.Reassign` diye bir metot **yoktur** — `EscalationRequest` nesnesinin `SuggestedAgentId`/`SuggestedAgentName`/`RoutingNote` alanları doğrudan mutasyona uğratılır.

**Akış:**
```
1. IEscalationSink.Get(escalationId) → bulunamazsa Error="Escalation not found."
2. agentId verilmişse IHumanAgentRegistry.Get(agentId) → bulunamazsa Error="Agent not found."
3. Eski SuggestedAgentId varsa → DecrementLoad(eski)
4. esc.SuggestedAgentId/SuggestedAgentName/RoutingNote güncellenir (mutasyon, in-place)
5. Yeni agent varsa → IncrementLoad(yeni)
6. RerouteResult(esc, null) döner
```

---

## Otomatik yük azaltma

```
IEscalationSink.RequestDecided (Status = Resolved veya Dismissed)
         │
         ▼
_loadTrackingHandler(esc)
         │
         ▼
IHumanAgentRegistry.DecrementLoad(esc.SuggestedAgentId)
```

Bu mekanizma sayesinde temsilci manuel olarak `Release` yapmadan eskalasyon kapanınca yük otomatik düşer.

---

## `IDisposable` implementasyonu

```csharp
public void Dispose()
{
    _escalations.RequestDecided -= _loadTrackingHandler;
}
```

---

## HumanAgent modeli

Bkz. [Model-Hitl.md](../domain/Model-Hitl.md#humanagent) — gerçek `HumanAgent`/`HumanAgentInput` alanları için.
