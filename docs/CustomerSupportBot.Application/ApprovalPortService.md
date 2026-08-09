# ApprovalPortService

**Dosya:** `Services/Approval/ApprovalPortService.cs`  
**Implements:** `IApprovalPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

HITL (Human-in-the-Loop) onay kuyruğunu yönetir. Pending onayları listeler, tek kayıt getirir ve onay/red kararı alır.

---

## Constructor bağımlılıkları

| Bağımlılık | Tür | Açıklama |
|-----------|-----|---------|
| `IApprovalQueue` | Driven port | Onay kayıtlarının persist edildiği kuyruk (Postgres) |
| `ILogger<ApprovalPortService>` | — | Loglama |

---

## Metodlar

Tüm metodlar **senkrondur** (`Task` yok, `CancellationToken` almazlar).

### `GetPending`

```csharp
IReadOnlyList<ApprovalRequest> GetPending()
```

`IApprovalQueue.GetPending()` sonucunu döner. Karar bekleyen tüm onaylar.

---

### `GetRecent`

```csharp
IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)
```

Son `count` kadar onay kaydını döner (hem pending hem karara bağlanmış).

---

### `Get`

```csharp
ApprovalRequest? Get(string id)
```

Tekil onay kaydını id'ye göre getirir. Bulunamazsa `null`.

---

### `Decide`

```csharp
bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null)
```

Onay/red kararı verir. `decidedBy` ve `reason` opsiyoneldir.

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
var pending = _approvalPort.GetPending();

// Onay ver
var success = _approvalPort.Decide(
    id: "req-123",
    approved: true,
    decidedBy: "admin@example.com",
    reason: "Stok kontrolü yapıldı, uygundur.");
```

---

## API endpoint'leri

Bkz. [Endpoints-Admin.md](../api/Endpoints-Admin.md) — HITL onay endpoint'lerinin gerçek route'ları için.
