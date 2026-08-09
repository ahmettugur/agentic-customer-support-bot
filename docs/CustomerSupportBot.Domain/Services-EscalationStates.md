# EscalationStates (State Pattern)

**Dosya:** `Services/EscalationStates.cs`

Escalation'ın yaşam döngüsünü **State Pattern** ile yönetir. Geçersiz geçişler tip seviyesinde engellenir.

---

## State diyagramı

```
        ┌──────────────────────────┐
        │                          ▼
   ┌─────────┐    ┌──────────────────┐
   │  Open   │───▶│  Acknowledged    │
   └────┬────┘    └─────────┬────────┘
        │                   │
        │                   ▼
        │           ┌──────────────┐
        ├──────────▶│  Resolved    │  (terminal)
        │           └──────────────┘
        │
        │           ┌──────────────┐
        └──────────▶│  Dismissed   │  (terminal)
                    └──────────────┘
```

**Terminal state'ler:** `Resolved` ve `Dismissed` — bu state'lerden çıkış yok.

---

## IEscalationState arayüzü

```csharp
public interface IEscalationState
{
    EscalationStatus Status { get; }
    IEscalationState Acknowledge();
    IEscalationState Resolve();
    IEscalationState Dismiss();
}
```

Her geçiş **yeni state instance** döner (immutable transitions).

---

## State implementasyonları

| State | Sonraki olası state'ler |
|---|---|
| `OpenEscalationState` | Acknowledged, Resolved, Dismissed |
| `AcknowledgedEscalationState` | Resolved, Dismiss |
| `ResolvedEscalationState` | — (terminal) |
| `DismissedEscalationState` | — (terminal) |

**Geçersiz geçişler:** Terminal state'lerde `Acknowledge()` çağrılırsa `InvalidOperationException` fırlatır.

```csharp
new ResolvedEscalationState().Acknowledge();
// → throws InvalidOperationException("Resolved escalation cannot be acknowledged")
```

---

## EscalationStateFactory

Enum → singleton state instance dönüşümü:

```csharp
public static IEscalationState From(EscalationStatus status) => status switch
{
    EscalationStatus.Open         => OpenEscalationState.Instance,
    EscalationStatus.Acknowledged => AcknowledgedEscalationState.Instance,
    EscalationStatus.Resolved     => ResolvedEscalationState.Instance,
    EscalationStatus.Dismissed    => DismissedEscalationState.Instance,
    _ => throw new ArgumentOutOfRangeException(...)
};
```

Singleton tutmak GC baskısı yaratmaz — state'ler stateless.

---

## Adapter kullanımı

`InMemoryEscalationSink` ve `PostgresEscalationSink` bu mantığı doğrudan kullanır:

```csharp
public void Acknowledge(string escalationId)
{
    var esc = _cache[escalationId];
    var currentState = EscalationStateFactory.From(esc.Status);
    var newState = currentState.Acknowledge();  // throw if invalid
    esc.Status = newState.Status;
}
```

Status geçişlerinin **tek doğruluk kaynağı** burada. Adapter'lar kuralı tekrar yazmaz.

---

## Neden State Pattern?

Alternatif yaklaşımlar:
- **Enum + if/switch**: Tüm kurallar her adapter'da tekrar yazılır
- **Database constraint**: Sadece DB seviyesinde — uygulama içi cache senkronize değil

State Pattern:
- ✅ Tek yerde tanımlı, her adapter aynı kuralı kullanır
- ✅ Yeni state eklemek kolay (örn. `ReassignedEscalationState`)
- ✅ Birim test edilebilir (state machine, DB veya cache olmadan)
