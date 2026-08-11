# EscalationRequest

**Dosya:** `Model/EscalationRequest.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `EscalationStatus` enum, `EscalationDecisionInput` class (aynı dosyada)

## 1. Ne İşe Yarar

Bot'un çözemediği bir sorun için **insan temsilciye eskalasyon talebi** oluşturur. Specialist agent'ın `postToolReflection.status = "needs_escalation"` dediğinde tetiklenir.

## 2. Hangi Amaçla Kullanılır

`IEscalationSink`'e yazılır, admin panelinde "Eskalasyonlar" listesinde görüntülenir. Bir insan temsilci acknowledge → resolve/dismiss yaparak çözüme kavuşturur. Skills-based routing ile en uygun temsilciye otomatik atanır.

> 💡 **Analiz notu:** Müşteri hizmetlerinde "Bunu üst yönetime iletin" talebi gibi — bot yetersiz kalınca insan devreye girer.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik |
| `SessionId` | `string?` | Hangi oturuma ait |
| `TraceId` | `string?` | Bağlı trace ID |
| `AgentName` | `string?` | Hangi agent eskalasyonu tetikledi |
| `UserQuery` | `string` | Kullanıcının orijinal sorgusu |
| `Reason` | `string` | LLM'in eskalasyon gerekçesi (postToolReflection'dan) |
| `MissingContext` | `List<string>` | Eksik bağlam / yapılamayanlar |
| `ResponseSummary` | `string?` | Son yanıt özeti (temsilci bağlam için) |
| `Status` | `EscalationStatus` | Open → Acknowledged → Resolved/Dismissed |
| `AssignedTo` | `string?` | Çözümü yapan temsilci |
| `Resolution` | `string?` | Çözüm notu |
| `RequiredSkills` | `List<string>` | Skills-based routing — gerekli yetkinlikler |
| `Priority` | `EscalationPriority` | Low / Normal / High / Critical |
| `SuggestedAgentId` | `string?` | Router'ın önerdiği temsilci |
| `SuggestedAgentName` | `string?` | Önerilen temsilci adı |
| `MatchScore` | `double` | Routing eşleşme skoru (0-1) |
| `RoutingNote` | `string?` | Neden bu temsilci önerildi |

## Bağlantılar

- [EscalationAction.md](EscalationAction.md) — Karar aksiyonları enum
- [HumanAgent.md](HumanAgent.md) — Temsilci profili
- [SpecialistReasoning.md](SpecialistReasoning.md) — Eskalasyonu tetikleyen PostToolReflection
