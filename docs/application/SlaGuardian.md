# SLA Guardian

**Dosyalar:**  
- `Services/SlaPortService.cs` — driving port impl  
- `Services/Sla/SlaPolicyEvaluator.cs` — ihlal hesaplama motoru  
- `Services/Sla/SlaOptions.cs` — konfigürasyon modeli  

## Genel bakış

SLA Guardian, bekleyen HITL onayları ve açık eskalasyonların yanıt sürelerini izler. Belirlenen eşikler aşılırsa uyarı eventi yayınlar; ihlal (breach) gerçekleşirse otomatik aksiyon alır.

```
SlaPortService (ISlaPort) ← API / BackgroundService
    ├── ScanOnce(opts) ─────────────────────────────────────────────┐
    │   ├── SlaPolicyEvaluator.EvaluateApproval(req, opts, sink, now) │
    │   └── SlaPolicyEvaluator.EvaluateEscalation(esc, opts, sink, now) │
    │                                                                │
    └── ISlaEventSink.Record(WarnEvent | BreachEvent) ◄─────────────┘
```

---

## SlaPortService

**Implements:** `ISlaPort`  
**Yaşam döngüsü:** Singleton

### Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `ISlaEventSink` | SLA olay kaydı deposu |
| `IApprovalQueue` | Pending onayların listesi |
| `IEscalationSink` | Açık eskalasyonların listesi |
| `IOptionsMonitor<SlaOptions>` | Canlı güncellenen konfigürasyon |

---

### `ScanOnce`

```csharp
void ScanOnce(SlaOptions opts)
```

Tüm pending onayları ve açık eskalasyonları tarar. Her kayıt için `SlaPolicyEvaluator` çağırır.

**Onay tarama adımları:**
```
1. IApprovalQueue.GetPending() → pending list
2. Her kayıt için SlaPolicyEvaluator.EvaluateApproval(...)
3. WarnEvent varsa ISlaEventSink.Record(warn)
4. BreachEvent varsa:
   - ISlaEventSink.Record(breach)
   - ApplyApprovalBreach(req, action)  ← AutoReject / AutoApprove
```

**`ApplyApprovalBreach`:**

| `SlaBreachAction` | Aksiyon |
|------------------|--------|
| `AutoReject` | `IApprovalQueue.Decide(id, approved=false, "SLA breach — auto-reject")` |
| `AutoApprove` | `IApprovalQueue.Decide(id, approved=true, "SLA breach — auto-approve")` |
| `None` | Sadece event kaydedilir; karar insan tarafında kalır |

**Eskalasyon tarama adımları:**
```
1. IEscalationSink.GetOpen() → açık list
2. Her kayıt için SlaPolicyEvaluator.EvaluateEscalation(...)
3. WarnEvent / BreachEvent varsa Record(...)
4. BreachEvent varsa priority boost uygula (NewPriority varsa)
```

---

### `GetStatus`

```csharp
SlaStatusResult GetStatus()
```

Anlık SLA durumunu döner:

**`SlaStatusResult` alanları:**

| Alan | Açıklama |
|------|---------|
| `Enabled` | SLA Guardian aktif mi |
| `PollIntervalSeconds` | Tarama frekansı |
| `Approvals.PendingCount` | Bekleyen onay sayısı |
| `Approvals.MaxAgeSeconds` | En uzun bekleyen onayın yaşı |
| `Approvals.WarnThreshold` | Uyarı eşiği (saniye) |
| `Approvals.BreachThreshold` | İhlal eşiği (saniye) |
| `Approvals.BreachCount` | Son 200 event'teki ihlal sayısı |
| `Escalations.OpenCount` | Açık eskalasyon sayısı |
| `Escalations.MaxAgeSeconds` | En uzun açık eskalasyonun yaşı |
| `Escalations.BreachCount` | Son 200 event'teki ihlal sayısı |

---

## SlaPolicyEvaluator

**Tür:** `public static class` — LLM-siz, saf determinizm

### `EvaluateApproval`

```csharp
ApprovalEvaluation EvaluateApproval(
    ApprovalRequest request,
    ApprovalSlaOptions options,
    ISlaEventSink sink,
    DateTime now)
```

**Karar ağacı:**
```
age = now - request.RequestedAt (saniye)

age >= BreachAfterSeconds?
  → sink.LastEmittedAt(breach) == null?  (idempotent — aynı breach iki kez kaydedilmez)
    → BreachEvent + action (AutoReject/AutoApprove/None)

age >= WarnAfterSeconds?
  → sink.LastEmittedAt(warn) == null?
    → WarnEvent
```

### `EvaluateEscalation`

Aynı mantık; breach sonrasında `BoostPriority`:

```
Low → Normal → High → Critical  (Critical en yüksekte sabit kalır)
```

---

## SlaOptions konfigürasyonu

`appsettings.json` → `SLA:` bölümü:

```json
{
  "SLA": {
    "Enabled": true,
    "PollIntervalSeconds": 5,
    "Approvals": {
      "WarnAfterSeconds": 20,
      "BreachAfterSeconds": 45,
      "OnBreach": "AutoReject"
    },
    "Escalations": {
      "WarnAfterSeconds": 60,
      "BreachAfterSeconds": 180,
      "BoostPriorityOnBreach": true
    }
  }
}
```

| Ayar | Varsayılan | Açıklama |
|------|-----------|---------|
| `Approvals.WarnAfterSeconds` | 20 | Onay uyarı eşiği |
| `Approvals.BreachAfterSeconds` | 45 | Onay ihlal eşiği |
| `Approvals.OnBreach` | `AutoReject` | `None` / `AutoReject` / `AutoApprove` |
| `Escalations.WarnAfterSeconds` | 60 | Eskalasyon uyarı eşiği |
| `Escalations.BreachAfterSeconds` | 180 | Eskalasyon ihlal eşiği |
| `Escalations.BoostPriorityOnBreach` | `true` | İhlalde priority artır |

---

## API endpoint'leri

```http
GET /sla/status        → GetStatus
GET /sla/events        → GetRecentEvents
POST /sla/scan         → ScanOnce (manuel tetikle)
```

---

## BackgroundService entegrasyonu

`SlaPortService.ScanOnce` periyodik olarak bir `BackgroundService` tarafından çağrılır. Detay için `CustomerSupportBot.Api` → `SlaGuardianBackgroundService`.
