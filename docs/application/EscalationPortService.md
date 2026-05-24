# EscalationPortService

**Dosya:** `Services/EscalationPortService.cs`  
**Implements:** `IEscalationPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Eskalasyon isteklerini yönetir ve `IEscalationSink` üzerindeki driven port event'lerini `IEscalationPort` driving port event'lerine köprüler. API katmanı bu servis üzerinden eskalasyonları listeler ve karar verir; admin SSE akışı da bu event'lerle beslenir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IEscalationSink` | Eskalasyon kayıt ve sorgulama deposu |

---

## Event köprüleme (Bridge Pattern)

Constructor'da `IEscalationSink` event'lerine abone olunur ve bu event'ler driving port event'leri olarak yeniden yayınlanır:

```
IEscalationSink.RequestCreated  →  IEscalationPort.EscalationCreated
IEscalationSink.RequestDecided  →  IEscalationPort.EscalationDecided
```

Bu sayede API katmanı `IEscalationSink`'i doğrudan bilmeden event akışına katılabilir.

---

## Metodlar

### `CreateAsync`

```csharp
Task<EscalationRequest> CreateAsync(
    string sessionId,
    string reason,
    EscalationPriority priority,
    string? agentName,
    CancellationToken ct = default)
```

Yeni eskalasyon isteği oluşturur. `IEscalationSink.Create` çağırır.

---

### `GetOpenAsync`

```csharp
Task<IReadOnlyList<EscalationRequest>> GetOpenAsync(string? sessionId = null, CancellationToken ct = default)
```

Açık (henüz karar verilmemiş) eskalasyonları döner. `sessionId` verilirse o oturuma filtrelenir.

---

### `GetRecentAsync`

```csharp
Task<IReadOnlyList<EscalationRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default)
```

Son `count` eskalasyonu döner (açık ve kapalı tümü).

---

### `GetAsync`

```csharp
Task<EscalationRequest?> GetAsync(string id, CancellationToken ct = default)
```

Tekil eskalasyon kaydını döner.

---

### `DecideAsync`

```csharp
Task<bool> DecideAsync(string id, bool resolved, string decidedBy, string? note, CancellationToken ct = default)
```

Eskalasyon kararı verir (çözüldü/reddedildi). `IEscalationSink.Decide` çağırır.

---

## EscalationPriority seviyeleri

| Seviye | Değer | Açıklama |
|--------|-------|---------|
| `Low` | 1 | Düşük öncelik |
| `Normal` | 2 | Normal öncelik |
| `High` | 3 | Yüksek öncelik (ComplaintAgent → otomatik High) |
| `Critical` | 4 | Kritik; SLA ihlalinde otomatik boost |

---

## EscalationPolicyService ile ilişki

`EscalationPortService` sadece CRUD ve event bridge işlevi görür. İş kuralları (hangi oturum eskalasyona alınsın, hangi temsilciye yönlendirilsin) `EscalationPolicyService` tarafından yönetilir. İkisi birbirinden bağımsız singleton'lardır.

---

## API endpoint'leri

```http
POST /escalation              → CreateAsync
GET  /escalation/open         → GetOpenAsync
GET  /escalation/recent       → GetRecentAsync
GET  /escalation/{id}         → GetAsync
POST /escalation/{id}/decide  → DecideAsync
```
