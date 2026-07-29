# EscalationPolicyService

**Dosya:** `Services/Escalation/EscalationPolicyService.cs`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Bir workflow tamamlandıktan sonra, specialist ajanlardan herhangi birinin `postToolReflection.status = "needs_escalation"` üretip üretmediğini kontrol eder. Eskalasyon gerekiyorsa:
1. Aday seçer (birden fazlaysa önceliğe göre)
2. Session bazlı duplicate kontrolü yapar
3. Skills-based routing ile uygun insan temsilcisi önerir
4. `IEscalationSink`'e yazar

## `ProcessPendingEscalations`

```csharp
public void ProcessPendingEscalations(
    ReasoningTrace trace,
    string userQuery,
    string finalResponse)
```

`CustomerSupportTeam.RunStreamingAsync` tarafından workflow bitiminde çağrılır.

### Adımlar

```
1. ApprovalOptions.EscalationEnabled = false? → Dön (eskalasyon devre dışı)

2. trace.SpecialistReasonings içinden NeedsEscalation olanları filtrele
   → Aynı ajandan birden fazla gelirse son olanı al (GroupBy + Last)

3. Hiç aday yoksa → Dön

4. Birden fazla aday varsa:
   → ComplaintAgent varsa ComplaintAgent tercih edilir (şikayetler öncelikli)
   → Yoksa son aday seçilir

5. Session dedup: Bu session için zaten açık eskalasyon var mı?
   → Varsa yeni oluşturma (log: "zaten açık eskalasyon var")

6. Her aday için EscalationRequest oluştur:
   - SessionId, TraceId, AgentName
   - UserQuery (tam metin)
   - Reason: reflection.HandoffReason ?? reflection.Summary
   - ResponseSummary: finalResponse ilk 500 karakter

7. ApplyRoutingDecision(req, trace, agentName):
   - SkillsBasedRouter.Decide(trace, agentName, profile) çağrılır
   - RequiredSkills, SuggestedAgentId/Name, MatchScore, RoutingNote güncellenir
   - Öncelik kuralları:
     * ComplaintAgent → Priority = High
     * MatchScore < 0.3 → Priority = High

8. IEscalationSink.Create(req)
9. IHumanAgentRegistry.IncrementLoad(suggestedAgentId)  ← yük sayacı güncelle
```

## Opsiyonel bağımlılıklar

```csharp
public EscalationPolicyService(
    IEscalationSink escalationSink,          // Zorunlu
    IOptions<ApprovalOptions> options,        // Zorunlu
    ISkillsBasedRouter? router = null,        // Opsiyonel
    IHumanAgentRegistry? agentRegistry = null, // Opsiyonel
    ICustomerProfileStore? profileStore = null, // Opsiyonel
    ISessionManager? sessionManager = null,   // Opsiyonel
    ILogger<EscalationPolicyService>? logger = null) // Opsiyonel
```

Opsiyonel bağımlılıklar null ise ilgili özellik atlanır:
- `_router == null` → routing kararı uygulanmaz, `SuggestedAgentId` boş kalır
- `_agentRegistry == null` → yük sayacı güncellenmez
- `_profileStore == null` → müşteri profili routing'e dahil edilmez

## `ApprovalOptions` yapılandırması

```json
{
  "HumanInTheLoop": {
    "EscalationEnabled": true
  }
}
```

`EscalationEnabled = false` ise `ProcessPendingEscalations` hiçbir şey yapmaz.

## `EscalationRequest` yapısı (özet)

```csharp
public class EscalationRequest
{
    string SessionId
    string TraceId
    string AgentName          // Hangi ajan eskalasyonu tetikledi
    string UserQuery
    string Reason             // Neden eskalasyon gerekti
    string? MissingContext    // Eksik bağlam bilgisi
    string ResponseSummary    // Bot'un son yanıtının özeti
    List<string> RequiredSkills     // Gerekli insan yetkinlikleri
    string? SuggestedAgentId        // Önerilen insan temsilcisi ID
    string? SuggestedAgentName
    double MatchScore               // Eşleşme skoru (0.0-1.0)
    string? RoutingNote
    EscalationPriority Priority     // Normal | High
}
```

## Session dedup neden önemli?

Aynı müşteri aynı oturumda birden fazla sorun yaşayabilir. Eğer dedup olmasaydı her `needs_escalation` çıktısında yeni bir kayıt oluşturulur, admin paneli çöplük haline gelirdi. Dedup sayesinde:

- Session başına **en fazla 1** açık eskalasyon kaydı olur
- Yeni sorunlar mevcut kaydın güncellenmesi ile takip edilir (bu kısım `IEscalationSink`'in sorumluluğundadır)

## Öncelik yükseltme kuralları

| Durum | Sonuç |
|-------|-------|
| `AgentName == "ComplaintAgent"` | Priority = High |
| `MatchScore < 0.3` ve mevcut priority Normal | Priority = High |
| Diğer | Priority değişmez |

`MatchScore < 0.3` durumu "uygun insan temsilcisi bulunamadı" anlamına gelir — böyle durumlarda kayıt High priority ile öne çıkarılır.
