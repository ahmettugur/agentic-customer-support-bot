# ApprovalPortService

**Dosya:** `Services/ApprovalPortService.cs`  
**Implements:** `IApprovalPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

HITL (Human-in-the-Loop) onay kuyruğunu yönetir. Pending onayları listeler, tek kayıt getirir ve onay/red kararı alır.

---

## Constructor bağımlılıkları

| Bağımlılık | Tür | Açıklama |
|-----------|-----|---------|
| `IApprovalQueue` | Driven port | Onay kayıtlarının persist edildiği kuyruk (Postgres/InMemory) |

---

## Metodlar

### `GetPendingAsync`

```csharp
Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
```

`IApprovalQueue.GetPending()` sonucunu döner. Karar bekleyen tüm onaylar.

---

### `GetRecentAsync`

```csharp
Task<IReadOnlyList<ApprovalRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default)
```

Son `count` kadar onay kaydını döner (hem pending hem karara bağlanmış).

---

### `GetAsync`

```csharp
Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default)
```

Tekil onay kaydını id'ye göre getirir. Bulunamazsa `null`.

---

### `DecideAsync`

```csharp
Task<bool> DecideAsync(string id, bool approved, string decidedBy, string? reason, CancellationToken ct = default)
```

Onay/red kararı verir.

**Önemli:** Karar vermeden önce isteğin `Pending` durumda olduğunu kontrol eder. Zaten karar verilmiş bir kayıt için `false` döner.

**Akış:**
```
1. IApprovalQueue.Get(id) → kayıt var mı?
2. request.Status == Pending? → değilse false dön
3. IApprovalQueue.Decide(id, approved, decidedBy, reason)
4. true dön
```

---

## Kullanım örneği

```csharp
// Admin panelinde bekleyen onayları listele
var pending = await _approvalPort.GetPendingAsync();

// Onay ver
var success = await _approvalPort.DecideAsync(
    id: "req-123",
    approved: true,
    decidedBy: "admin@example.com",
    reason: "Stok kontrolü yapıldı, uygundur.");
```

---

## API endpoint'leri

```http
GET  /hitl/pending          → GetPendingAsync
GET  /hitl/recent?count=50  → GetRecentAsync
GET  /hitl/{id}             → GetAsync
POST /hitl/{id}/decide      → DecideAsync
```
