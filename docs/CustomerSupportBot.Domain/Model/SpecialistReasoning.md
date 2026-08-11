# SpecialistReasoning

**Dosya:** `Model/SpecialistReasoning.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `PreToolCheck`, `PostToolReflection`, `TaskCompletionStatus` enum (aynı dosyada)

## 1. Ne İşe Yarar

Specialist agent'ların (OrderAgent, ProductAgent, ComplaintAgent) tool çağrısı etrafındaki **yapılandırılmış reasoning**ini taşır: tool çağrısı öncesi parametre kontrolü (PreToolCheck) ve tool çağrısı sonrası yansıtma (PostToolReflection).

## 2. Hangi Amaçla Kullanılır

Agent mesajının başında/sonunda JSON olarak üretilir, `SpecialistReasoningParser` ile parse edilir. Trace'e yansır (debug panelinde görüntülenir). `PostToolReflection.Status` routing kararlarını etkiler — `needs_escalation` ise eskalasyon tetiklenir, `needs_followup` ise başka agent'a handoff yapılabilir.

> 💡 **Analiz notu:** Bir doktorun ameliyat öncesi kontrol listesi (preToolCheck) ve ameliyat sonrası raporu (postToolReflection) gibi düşün.

## 3. Metotlar / Üyeler

### SpecialistReasoning

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `AgentName` | `string` | Hangi agent ürettiyse (ör. "OrderAgent") |
| `PreToolCheck` | `PreToolCheck?` | Tool öncesi parametre doğrulaması |
| `ResultConfidence` | `double?` | Tool sonucu güven skoru (null = tool çağrılmadı) |
| `ResultNotes` | `string?` | Sonuç özeti |
| `PostToolReflection` | `PostToolReflection?` | Tool sonrası yansıtma |

### PreToolCheck

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `RequiredParams` | `List<string>` | Tool'un gerektirdiği parametreler |
| `CollectedParams` | `List<string>` | Toplamış olduğu parametreler |
| `MissingParams` | `List<string>` | Eksik parametreler |
| `CanProceed` | `bool` | Tool çağrılabilir mi? |
| `Reasoning` | `string` | Kararın gerekçesi |
| `Confidence` | `double` | Güven skoru (0.0-1.0) |

### PostToolReflection

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `TaskComplete` | `bool` | Kullanıcının ihtiyacı giderildi mi? |
| `Status` | `string` | "done", "needs_followup", "needs_escalation", "failed", "partial", "pending_approval" |
| `StatusEnum` | `TaskCompletionStatus` | **Computed** — Status'un type-safe karşılığı |
| `HandoffSuggestion` | `string?` | Başka agent'a devret önerisi |
| `HandoffReason` | `string` | Handoff gerekçesi |
| `MissingContext` | `List<string>` | Eksik bağlam |
| `Summary` | `string` | İşlem sonucu özeti |

### TaskCompletionStatus Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Done` | Görev tamamlandı |
| `NeedsFollowUp` | Ek bilgi/eylem gerekli |
| `NeedsEscalation` | İnsan temsilciye eskalasyon gerekli |
| `Failed` | Görev başarısız |
| `Partial` | Kısmen tamamlandı |
| `PendingApproval` | HITL onay bekliyor |

## Bağlantılar

- [ReasoningTrace.md](ReasoningTrace.md) — Trace'e yazılan specialist reasoning
- [EscalationRequest.md](EscalationRequest.md) — `needs_escalation` durumunda tetiklenen eskalasyon
- [ToolResult.md](ToolResult.md) — Tool sonuç zarfı
