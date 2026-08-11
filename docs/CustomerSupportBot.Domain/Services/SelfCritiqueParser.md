# SelfCritiqueParser

**Dosya:** `Services/SelfCritiqueParser.cs`  
**Tür:** `static class` — tamamen deterministik

## 1. Ne İşe Yarar

ResponseAgent'ın çıktısındaki `[SELF_CRITIQUE]` JSON bloğunu `SelfCritique` nesnesine dönüştürür.

## 2. Hangi Amaçla Kullanılır

ResponseAgent prompt'unda yanıttan sonra kalite değerlendirmesi JSON bloğu üretmesi istenir. Bu parser o bloğu çıkarır. Parse edilemezse null döner — tur etkilenmez.

## 3. Metotlar

| Metot | Açıklama |
|-------|----------|
| `TryParse(string?)` | Agent çıktısı → SelfCritique?. Parse başarısızsa null. |

## Bağlantılar

- [../Model/SelfCritique.md](../Model/SelfCritique.md) — Üretilen model
