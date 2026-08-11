# PlanningResultParser

**Dosya:** `Services/PlanningResultParser.cs`  
**Tür:** `static class` — LLM çağrısı yapmaz, tamamen deterministik

## 1. Ne İşe Yarar

PlanningAgent'ın çıktısındaki JSON bloğunu parse ederek `PlanningResult` nesnesine dönüştürür.

## 2. Hangi Amaçla Kullanılır

PlanningAgent mesaj çıktısında ````json { ... }```` fence'leri içinde yapılandırılmış JSON üretir. Bu parser o JSON'u çıkarır, deserialize eder ve `PlanningResult` nesnesi olarak döner.

> 💡 **Analiz notu:** LLM'ler serbest metin üretir ama kodumuz yapılandırılmış veri bekler. Parser bu iki dünya arasında köprüdür — LLM'in metin çıktısını güvenle programatik olarak okuyabilmemizi sağlar.

## 3. Metotlar

| Metot | İmza | Açıklama |
|-------|------|----------|
| `TryParse` | `PlanningResult? TryParse(string?)` | JSON bulunamaz/parse edilemezse null döner |

## Bağlantılar

- [../Model/PlanningResult.md](../Model/PlanningResult.md) — Üretilen model
- [ReasoningResultParser.md](ReasoningResultParser.md) — Benzer parser
