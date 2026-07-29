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
public class ApprovalRequest
{
    public string Id { get; set; }                    // Guid.NewGuid()[..12]
    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string ToolName { get; set; } = "";
    public string? AgentName { get; set; }

    public Dictionary<string, object?> Parameters { get; set; } = new();
    public string? UserQuery { get; set; }
    public string? Justification { get; set; }         // Agent neden onay istiyor

    public DateTime RequestedAt { get; set; }           // NOT: "CreatedAt" değil
    public int TimeoutSeconds { get; set; } = 60;

    // Karar
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }             // Admin user id
    public string? DecisionReason { get; set; }
}

public enum ApprovalStatus { Pending, Approved, Rejected, Expired }
```

Tüm property'ler mutable (`get; set;`) — `sealed`/`init` değil. Zaman damgası alanının adı **`RequestedAt`**'tir, `CreatedAt` değil.

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
public class EscalationRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string? AgentName { get; set; }
    public string UserQuery { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> MissingContext { get; set; } = new();
    public string? ResponseSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public EscalationStatus Status { get; set; } = EscalationStatus.Open;
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }

    // Skills-based routing
    public List<string> RequiredSkills { get; set; } = new();
    public EscalationPriority Priority { get; set; } = EscalationPriority.Normal;
    public string? SuggestedAgentId { get; set; }
    public string? SuggestedAgentName { get; set; }
    public double MatchScore { get; set; }
    public string? RoutingNote { get; set; }
}

public enum EscalationStatus { Open, Acknowledged, Resolved, Dismissed }
public enum EscalationPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }
```

### Yaşam döngüsü

`Open → Acknowledged → Resolved` (veya `Dismissed`). Geçişler `EscalationStateFactory` ile yönetilir (bkz. [Services-EscalationStates.md](Services-EscalationStates.md)).

`DecidedAt` field'ı kaldırıldı — yerine `AcknowledgedAt` (acknowledge anı) ve `ResolvedAt` (çözüm anı) ayrı ayrı izleniyor.

### EscalationDecisionInput

```csharp
public class EscalationDecisionInput
{
    /// <summary>"acknowledge" | "resolve" | "dismiss"</summary>
    public string Action { get; set; } = "resolve";
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
}
```

Admin endpoint'i `/escalations/{id}/decide` bu modeli alır.

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
public class HumanAgent
{
    public string Id { get; set; }                            // Guid.NewGuid()[..8]
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;

    // Skills-based routing
    public List<string> Skills { get; set; } = new();        // Normalize lowercase
    public List<string> Languages { get; set; } = new();     // ISO 639-1: ["tr", "en"]

    // Load balancing
    public int MaxConcurrentLoad { get; set; } = 5;
    public int CurrentLoad { get; set; }                     // Atomic, locked

    // Routing tie-break
    public int Priority { get; set; }                        // Yüksek öncelikli ajan

    public DateTime CreatedAt { get; set; }
    public DateTime? LastAssignedAt { get; set; }
}

/// <summary>Admin endpoint'i için temsilci create/update input modeli — tüm alanlar nullable (partial update).</summary>
public class HumanAgentInput
{
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public List<string>? Skills { get; set; }
    public List<string>? Languages { get; set; }
    public bool? IsActive { get; set; }
    public int? MaxConcurrentLoad { get; set; }
    public int? Priority { get; set; }
}
```

`sealed` değildir; `Id` dahil tüm property'ler mutable (`get; set;`).

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

`SkillsBasedRouter`'ın çıktısı — `Model/HumanAgent.cs` içinde, Domain katmanında tanımlıdır (Application'da değil):

```csharp
public class RoutingDecision
{
    public string? SuggestedAgentId { get; set; }
    public string? SuggestedAgentName { get; set; }
    public double MatchScore { get; set; }
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string? Note { get; set; }
}
```

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
