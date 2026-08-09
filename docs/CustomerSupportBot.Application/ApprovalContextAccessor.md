# ApprovalContextAccessor

**Dosya:** `Services/Approval/ApprovalContextAccessor.cs`  
**Implements:** `IApprovalContextAccessor`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

HITL onay bağlamını (session, trace, query) `async` çağrı zinciri boyunca taşır. `AsyncLocal<T>` kullandığı için her async akış kendi bağlamını izole şekilde taşır — paralel workflow'lar birbirini etkilemez.

---

## AsyncLocal<T> nedir?

`AsyncLocal<T>`, thread-local belleğin async karşılığıdır. `await` ile asenkron olarak devam eden kod bloğu, kendi `AsyncLocal` değerini devralır; farklı `async` zincirlerinin değerleri birbirinden bağımsızdır.

```
Request A:  SetScope(sessionA) → işlem → Dispose
Request B:  SetScope(sessionB) → işlem → Dispose
                ↓
Her ikisi Singleton olan ApprovalContextAccessor'ı paylaşır ama birbirinin bağlamını görmez.
```

---

## `IApprovalContextAccessor` arayüzü

```csharp
public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}
```

---

## `Context`

```csharp
public ApprovalContext? Context => _current.Value;
```

Mevcut async akışın `ApprovalContext`'ini döner. Scope açılmamışsa `null`.

---

## `SetScope`

```csharp
public IDisposable SetScope(string? sessionId, string? traceId, string? userQuery)
```

Yeni bir bağlam başlatır. Döndürülen `IDisposable.Dispose()` çağrıldığında önceki bağlam restore edilir.

**Kullanım (using ile):**
```csharp
using var approvalScope = _approvalContext.SetScope(sessionId, traceId, userQuery);
// Bu using bloğu içinde _approvalContext.Context → yeni bağlam
// Blok bitince → önceki bağlam restore edilir
```

---

## `ContextScope` (private iç sınıf)

```csharp
private sealed class ContextScope : IDisposable
{
    private readonly ApprovalContext? _previous;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _current.Value = _previous;  // önceki bağlamı geri yükle
    }
}
```

İç içe scope'larda önceki değer korunur: scope1 → scope2 → scope2.Dispose() → scope1 bağlamı döner.

---

## ApprovalContext modeli

```csharp
public record ApprovalContext(
    string? SessionId,
    string? TraceId,
    string? UserQuery
);
```

---

## Kullanıldığı yerler

| Servis | Kullanım |
|--------|---------|
| `ChatPortService.HandleAsync` | Her chat isteği için scope açar |
| `ReplanService.ExecuteAsync` | Replan pipeline'ı için scope açar |
| `ApprovalGateService` (Adapters.Agents) | `Context` okur — HITL onay isteğinde sessionId/query'yi kullanır |

---

## Neden Singleton?

`AsyncLocal<T>` Singleton içinde de async-izole çalışır. Servis singleton olduğu hâlde her async çağrı kendi bağlamını taşır. Scoped yapmak aynı sonucu vermez — gereksiz kapsam karmaşıklığı ekler.
