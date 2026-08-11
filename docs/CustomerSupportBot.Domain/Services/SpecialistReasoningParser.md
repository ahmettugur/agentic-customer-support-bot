# SpecialistReasoningParser

**Dosya:** `Services/SpecialistReasoningParser.cs`  
**Tür:** `static class` — tamamen deterministik

## 1. Ne İşe Yarar

Specialist agent'ların (OrderAgent, ProductAgent, ComplaintAgent) çıktısındaki preToolCheck ve postToolReflection JSON bloklarını `SpecialistReasoning` nesnesine dönüştürür.

## 2. Metotlar

| Metot | Açıklama |
|-------|----------|
| `TryParse(string?, string)` | Agent çıktısı + agent adı → SpecialistReasoning. Parse edilemezse null. |

## Bağlantılar

- [../Model/SpecialistReasoning.md](../Model/SpecialistReasoning.md) — Üretilen model
