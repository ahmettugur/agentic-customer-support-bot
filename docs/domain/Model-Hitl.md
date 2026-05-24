# HITL Modelleri (Human-in-the-Loop)

**Dosyalar:**
- `Model/ApprovalRequest.cs`
- `Model/EscalationRequest.cs`
- `Model/EscalationAction.cs`
- `Model/HumanAgent.cs`

HITL akışında kullanılan core domain modeller — onay, eskalasyon, insan agent yönetimi.

---

## ApprovalRequest

Yüksek riskli tool çağrılarında (`order_placement_tool`, `complaint_registration_tool`) **admin onayı** gerekir. Bu modeller pending onayı temsil eder.

```csharp
public sealed class ApprovalRequest
{
    public string Id { get; init; }
    public string SessionId { get; init; }
    public string? TraceId { get; init; }
    public string ToolName { get; init; }
    public string AgentName { get; init; }

    public Dictionary<string, object?> Parameters { get; init; }
    public string UserQuery { get; init; }
    public string? Justification { get; init; }       // Agent neden onay istiyor

    public DateTime CreatedAt { get; init; }
    public int TimeoutSeconds { get; init; }

    // Karar
    public ApprovalStatus Status { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }            // Admin user id
    public string? DecisionReason { get; set; }
}

public enum ApprovalStatus { Pending, Approved, Rejected, Expired }
```

### Status geçişleri

```
Pending ──Approve──▶ Approved
   │ ────Reject ──▶ Rejected
   └─Timeout──────▶ Expired
```

`SlaGuardian` timeout'u bekleyen approval'ları periyodik tarar ve config'e göre `AutoApprove`/`AutoReject`/`None` davranışını uygular.

### High-risk tool listesi

`HumanInTheLoop:ToolsRequiringApproval` (appsettings.json):
- `order_placement_tool`
- `complaint_registration_tool`

Bu tool'lar approval gerektirir; `WellKnown.ToolNames`'teki diğerleri (read-only) gerektirmez. Kontrol `ApprovalGateService` içinde `_options.ToolsRequiringApproval.Contains(toolName)` ile yapılır.

---

## EscalationRequest

Bot bir konuyu çözemediğinde veya kullanıcı insan istediğinde oluşturulan **eskalasyon kaydı**:

```csharp
public sealed class EscalationRequest
{
    public string Id { get; init; }
    public string SessionId { get; init; }
    public string UserQuery { get; init; }
    public string Reason { get; init; }
    public List<string> MissingContext { get; init; } = new();
    public DateTime CreatedAt { get; init; }

    // Durum
    public EscalationStatus Status { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? AssignedTo { get; set; }            // HumanAgent id
    public string? Resolution { get; set; }

    // Skills-based routing
    public List<string> RequiredSkills { get; init; } = new();   // ["complaint", "vip", "tr"]
    public EscalationPriority Priority { get; init; } = EscalationPriority.Normal;
    public string? SuggestedAgentId { get; set; }     // Router'ın önerdiği
    public string? SuggestedAgentName { get; set; }
    public double MatchScore { get; set; }             // Router skor
    public string? RoutingNote { get; set; }
}

public enum EscalationStatus { Open, Acknowledged, Resolved, Dismissed }
public enum EscalationPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }
```

### Yaşam döngüsü

`Open → Acknowledged → Resolved` (veya `Dismissed`). Geçişler `EscalationStateFactory` ile yönetilir (bkz. [Services-EscalationStates.md](Services-EscalationStates.md)).

### RequiredSkills ne işe yarar?

`SkillsBasedRouter` bu listeyi alıp **en uygun agent**'ı bulur:

```
Escalation.RequiredSkills = ["complaint", "tr", "vip"]
Agent A: Skills=["complaint","tr"]              → match 2/3
Agent B: Skills=["complaint","tr","vip"]        → match 3/3 ✓
Agent C: Skills=["order","en"]                  → match 0/3
```

Detay için [Application SkillsBasedRouter](../application/SkillsBasedRouter.md).

---

## EscalationAction

Kararları tip-güvenli temsil eder:

```csharp
public enum EscalationAction
{
    Acknowledge,
    Resolve,
    Dismiss
}
```

`IEscalationPort.DecideAsync(id, action)` bu enum'u alır; `EscalationStateFactory` ile state geçişi yapılır.

---

## HumanAgent

Live takeover yapacak veya escalation çözecek **insan agent** kaydı:

```csharp
public sealed class HumanAgent
{
    public string Id { get; init; }
    public string DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }

    // Skills-based routing
    public List<string> Skills { get; set; } = new();        // Normalize lowercase
    public List<string> Languages { get; set; } = new();     // ISO 639-1: ["tr", "en"]

    // Load balancing
    public int MaxConcurrentLoad { get; set; } = 5;
    public int CurrentLoad { get; set; }                     // Atomic, locked

    // Routing tie-break
    public int Priority { get; set; }                        // Yüksek öncelikli ajan
}
```

### Skill normalize kuralı

`Skills` listesi her zaman **lowercase**:
```
"Complaint" → "complaint"
"VIP" → "vip"
```

`SkillsBasedRouter` karşılaştırmada case-insensitive olmak için bunu varsayar.

### CurrentLoad yönetimi

- **TakeOver**: `IncrementLoad(agentId)` — atomic +1
- **Release**: `DecrementLoad(agentId)` — atomic -1 (negatife düşmez)
- `CurrentLoad >= MaxConcurrentLoad` ise yeni atama yapılmaz

Postgres adapter'da per-agent `SemaphoreSlim(1,1)` + DB UPDATE; InMemory'de `lock` kullanılır.

### Email + LinkedAgentId

Eğer agent aynı zamanda admin panele login oluyorsa, `Users` tablosunda karşılığı vardır (`UserInfo.LinkedAgentId = agent.Id`).

---

## RoutingDecision

`SkillsBasedRouter`'ın çıktısı (Domain'de yok, Application'da; ama HumanAgent burada tanımlandığı için bağlantı için referans veriyoruz):

```
Skill match score  +
Language weight    +
Load factor        +
Priority boost
= MatchScore
```

`EscalationRequest.SuggestedAgentId` / `MatchScore` field'larına yazılır.

---

## Akış örnekleri

### Approval akışı

```
OrderAgent: order_placement_tool çağıracak
   ↓
IApprovalGate.RequestAsync(...)
   → ApprovalRequest oluştur (Pending)
   → Admin'e bildirim (Redis pub/sub)
   ↓
Admin: Approve / Reject
   → DecideAsync(id, true/false)
   ↓
OrderAgent devam eder / iptal eder
```

### Escalation akışı

```
Bot: "Çözemiyorum, insana yönlendireceğim"
   ↓
IEscalationSink.Create(reason, missingContext, requiredSkills)
   → EscalationRequest (Open)
   → SkillsBasedRouter agent öner
   → SuggestedAgentId set edilir
   ↓
Admin paneli: agent görür → TakeOver
   → ChatMode.Human
   → IncrementLoad
   → Escalation.Acknowledge
   ↓
Sohbet bitince → Release → DecrementLoad → Escalation.Resolve
```

---

## Bağlantılar

- [Services-EscalationStates.md](Services-EscalationStates.md) — State machine detayı
- [Model-Session.md](Model-Session.md) — `ChatMode.Human` takeover
- [WellKnown.md](WellKnown.md) — `ToolNames`, intent listesi
