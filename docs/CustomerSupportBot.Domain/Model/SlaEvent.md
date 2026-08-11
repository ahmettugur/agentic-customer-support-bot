# SlaEvent

**Dosya:** `Model/SlaEvent.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

SLA Guardian'ın ürettiği **uyarı (warn) veya ihlal (breach) olayını** temsil eder. Bir onay veya eskalasyon belirli sürede çözülmezse tetiklenir.

## 2. Hangi Amaçla Kullanılır

`SlaPortService` belirli aralıklarla bekleyen onayları ve eskalasyonları tarar. Konfigüre edilmiş süreler aşılırsa `SlaEvent` oluşturulur ve admin paneline bildirim gönderilir.

> 💡 **Analiz notu:** Bir pizzacının "30 dakikada gelmezse bedava" garantisi gibi — belirlenen SLA süreleri aşıldığında alarm çalar.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik |
| `Timestamp` | `DateTime` | Olay zamanı |
| `Kind` | `string` | "approval" veya "escalation" |
| `Severity` | `string` | "warn" (uyarı) veya "breach" (ihlal) |
| `TargetId` | `string` | İlgili approval/escalation ID |
| `AgeSeconds` | `int` | Kaç saniyedir bekliyor |
| `Action` | `string?` | Yapılan aksiyon (ör. auto-reject) |
| `Note` | `string?` | Ek not |

## Bağlantılar

- [ApprovalRequest.md](ApprovalRequest.md) — SLA takibi yapılan onaylar
- [EscalationRequest.md](EscalationRequest.md) — SLA takibi yapılan eskalasyonlar
