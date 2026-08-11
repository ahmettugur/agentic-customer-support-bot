# EscalationAction

**Dosya:** `Model/EscalationAction.cs`  
**Tür:** `enum`

## 1. Ne İşe Yarar

Eskalasyon kaydına uygulanabilecek aksiyonları type-safe olarak temsil eder. Eski string tabanlı "acknowledge"/"resolve"/"dismiss" yerine kullanılır.

## 2. Enum Değerleri

| Değer | Açıklama |
|-------|----------|
| `Acknowledge` | Temsilci aldı ama henüz çözmedi |
| `Resolve` | Sorun çözüldü |
| `Dismiss` | Geçersiz bulundu (yanlış eskalasyon) |

## Bağlantılar

- [EscalationRequest.md](EscalationRequest.md) — Bu aksiyonların uygulandığı model
