# JsonExtensions

## Ne İşe Yarar
`System.Text.Json.JsonElement` üzerinde güvenli (null-safe) erişim sağlayan extension metotlarıdır.

## Hangi Amaçla Kullanılır
Trace detay panelinde ve replay bileşeninde, backend'den gelen `JsonElement` türündeki dinamik verilerin (reasoning, planning, specialist reasoning vb.) alanlarına güvenli şekilde erişmek için kullanılır.

## Sorumlulukları
- Property mevcut değilse veya beklenmeyen tipte ise `null` döndürmek (exception fırlatmamak).
- String, bool, int ve string array türleri için kısa yol erişim sunmak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan bileşenler**: `Components/TraceDetailPanel.razor`, `Pages/Replay.razor`.
- **İlişkili model**: [TraceDetailModels](../Models/TraceDetailModels.md) — `TraceDetail.Reasoning`, `TraceDetail.Planning` gibi `JsonElement?` alanları.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Backend'den gelen trace verileri yarı-yapılandırılmış (semi-structured) JSON'dur; her zaman aynı property'leri içermez. `TryGetProperty` + null coalescing deseni, `KeyNotFoundException` yerine `null` döndürerek UI'ın çökmesini önler.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `TryGetProp(el, key)` | String property okur; yoksa `null`. |
| `TryGetBool(el, key)` | Boolean property okur; yoksa `null`. |
| `TryGetInt(el, key)` | Int32 property okur; yoksa `null`. |
| `TryGetStringArray(el, key)` | String array property okur; yoksa `null`. |

## Bağımlılıklar
- `System.Text.Json`
