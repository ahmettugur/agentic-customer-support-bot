# Icon

**Dosya:** `Components/Icon.razor`

## Ne İşe Yarar

Personel arayüzünün tek tip çizgi ikon setidir (Lucide çizimleri). `Name` parametresine göre satır içi SVG üretir.

## Hangi Amaçla Kullanılır

Emojilerin yerine kullanılır: emojiler her işletim sisteminde farklı görünüyor ve çizgi ikonlarla karışınca
panel tutarsız duruyordu. Renk `currentColor`'dan gelir; ikonlar dekoratiftir (`aria-hidden`) — anlamı yanındaki
metin ya da düğmenin `aria-label`'ı taşır.

## Parametreler

| Parametre | Varsayılan | Açıklama |
|-----------|------------|----------|
| `Name` | — | İkon adı (ör. `inbox`, `alert`, `message`, `check`, `x`, `refresh`, `play`, `pause`, `more`, `search` …). Tanınmayan ad boş bir daire çizer. |
| `Size` | 16 | Piksel. |
| `StrokeWidth` | `"1.9"` | Çizgi kalınlığı — kültürden bağımsız yazılsın diye string. |
| `Class` | — | Ek CSS sınıfı. |
