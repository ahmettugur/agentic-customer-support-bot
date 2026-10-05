# CustomerSupportBot.Application.Services.SavedReplies

Ekip çapında hazır yanıt kütüphanesi. Tasarım:
[docs/superpowers/specs/2026-10-05-saved-replies-design.md](../../../superpowers/specs/2026-10-05-saved-replies-design.md).

## `SavedReplyService` (`ISavedReplyPort`)

| İşlem | Davranış |
|---|---|
| `ListAsync(query)` | Başlığa göre Türkçe (tr-TR) sıralı liste; `query` başlık, metin ve kısayolda büyük/küçük harf duyarsız arar. |
| `CreateAsync(input, createdBy)` | Doğrular, kısayolu normalleştirir, benzersizliği denetler. |
| `UpdateAsync(id, input)` | Aynı doğrulama; kaydın kendi kısayolu çakışma sayılmaz. |
| `DeleteAsync(id)` | Bulunamazsa `false`. |

Sonuç `SavedReplyStatus`: `Ok`, `Invalid` (400), `NotFound` (404), `DuplicateShortcut` (409).

## Kurallar

- Başlık ≤ 100, metin ≤ 2000 karakter; ikisi de zorunlu, baştaki/sondaki boşluk kırpılır.
- Kısayol isteğe bağlı, ≤ 40 karakter; yalnız küçük harf, rakam, `-`, `_` ve Türkçe harfler.
- Kısayol normalleştirme: `I` ve `İ` önce `i`'ye çevrilir, sonra Türkçe küçük harf. Böylece `IADE`,
  `İADE` ve `iade` aynı kısayoldur (yalnız Türkçe küçük harf `IADE`'yi `ıade` yapardı).
- Benzersizlik iki katmanlı: önce `ShortcutExistsAsync`, eşzamanlı eklemede veritabanının benzersiz
  indeksi (`SavedReplyShortcutConflictException` → `DuplicateShortcut`).
