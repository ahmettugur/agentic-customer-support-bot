# ConfidenceLevel

**Dosya:** `Model/ConfidenceLevel.cs`  
**Tür:** `enum`

## 1. Ne İşe Yarar

Reasoning güven seviyesini type-safe olarak temsil eder. Eski string tabanlı "yüksek/orta/düşük" karşılaştırmaları yerine enum kullanımını sağlar.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.ConfidenceLevel` computed property'si `ConfidenceScore`'dan bu enum'u türetir. Kod içinde güven seviyesi kontrolü yapılırken string karşılaştırması yerine enum kullanılır.

## 3. Enum Değerleri

| Değer | Score Aralığı | Açıklama |
|-------|---------------|----------|
| `Low` | < 0.5 | Düşük güven — clarification gerekebilir |
| `Medium` | 0.5 – 0.75 | Orta güven — devam edilebilir ama dikkatli |
| `High` | ≥ 0.75 | Yüksek güven — doğrudan aksiyon alınabilir |

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Bu enum'u kullanan model
