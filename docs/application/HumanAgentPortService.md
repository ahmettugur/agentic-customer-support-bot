# HumanAgentPortService

**Dosya:** `Services/HumanAgentPortService.cs`  
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

**Constructor'da:** `IEscalationSink.RequestDecided` ve `RequestDismissed` event'lerine `_loadTrackingHandler` abone olunur. Eskalasyon çözüldüğünde veya reddedildiğinde ilgili temsilcinin yükü otomatik azaltılır.

---

## Metodlar

### `GetAllMergedAsync`

```csharp
Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default)
```

`IHumanAgentRegistry`'deki kayıtlı temsilciler ile linked users'ı birleştirir. Sonuç `DisplayName`'e göre sıralanır.

---

### `GetAgentAsync`

Tekil temsilci kaydını döner.

---

### `CreateAgentAsync / UpdateAgentAsync / DeleteAgentAsync`

CRUD işlemleri. `IHumanAgentRegistry` metodlarını çağırır.

---

### `IncrementLoadAsync / DecrementLoadAsync`

```csharp
Task IncrementLoadAsync(string agentId, CancellationToken ct = default)
Task DecrementLoadAsync(string agentId, CancellationToken ct = default)
```

Temsilcinin `CurrentLoad` sayacını artırır/azaltır. Maksimum `MaxConcurrentLoad`'u aşan increment'lar görmezden gelinir.

`ChatSessionPortService.TakeOverAsync` ve `ReleaseAsync` bu metodları çağırır.

---

### `RerouteEscalationAsync`

```csharp
Task RerouteEscalationAsync(
    string escalationId,
    string fromAgentId,
    string toAgentId,
    CancellationToken ct = default)
```

Bir eskalasyonu eski temsilciden yeni temsilciye aktarır.

**Akış:**
```
1. IHumanAgentRegistry.DecrementLoad(fromAgentId)
2. IHumanAgentRegistry.IncrementLoad(toAgentId)
3. IEscalationSink.Reassign(escalationId, toAgentId)
```

---

## Otomatik yük azaltma

```
IEscalationSink.RequestDecided/RequestDismissed
         │
         ▼
_loadTrackingHandler(agentId)
         │
         ▼
IHumanAgentRegistry.DecrementLoad(agentId)
```

Bu mekanizma sayesinde temsilci manuel olarak `Release` yapmadan eskalasyon kapanınca yük otomatik düşer. Memory leak'i önlemek için `Dispose()` metodunda event abonelikleri iptal edilir.

---

## `IDisposable` implementasyonu

```csharp
public void Dispose()
{
    _sink.RequestDecided  -= _loadTrackingHandler;
    _sink.RequestDismissed -= _loadTrackingHandler;
}
```

---

## HumanAgent modeli

```csharp
public class HumanAgent
{
    string Id;
    string DisplayName;
    IReadOnlyList<string> Skills;       // ["complaint", "order", "tr"]
    IReadOnlyList<string> Languages;    // ["tr", "en"]
    int CurrentLoad;
    int MaxConcurrentLoad;
    int Priority;                       // SkillsBasedRouter'da kullanılır
    bool IsActive;
    string? LinkedUserId;               // Auth user bağlantısı
}
```
