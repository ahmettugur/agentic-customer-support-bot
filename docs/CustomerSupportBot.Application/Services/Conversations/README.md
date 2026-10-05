# CustomerSupportBot.Application.Services.Conversations

Temsilcinin canlı sohbeti kapanış nedeni, etiketler ve notla kapatması ("wrap-up"). Tasarım:
[docs/superpowers/specs/2026-10-05-conversation-disposition-design.md](../../../superpowers/specs/2026-10-05-conversation-disposition-design.md).

## `ConversationClosingService` (`IConversationClosingPort`)

| Üye | Davranış |
|---|---|
| `GetOptionsAsync` | `RequireReason`, nedenler (`EffectiveReasons`), önerilen etiketler (en sık 10; okunamazsa boş). |
| `CloseAsync(sessionId, input, closedBy, agentId)` | Doğrula → `IChatSessionPort.ReleaseAsync` (Bot moduna dönüş, eskalasyonların çözülmesi, temsilci yükü) → kaydı yaz. |

Sonuç `ConversationClosingStatus`: `Ok`, `Invalid` (400 — sohbet **kapanmaz**), `NotLive` (404).
Neden zorunlu değilken neden/etiket/not hiç verilmezse kayıt yazılmaz. Kayıt yazılamazsa sohbet yine
kapanmış olur: sonuç `Ok` + `Error` (uçta `warning`).

## Kurallar

- Neden yapılandırılmış listede olmalı (büyük/küçük harf duyarsız; kayda yapılandırılmış kod yazılır).
- Etiket: Türkçe küçük harf (`I`/`İ` → `i`), boşluklar `-`; harf/rakam/`-`/`_`; ≤ 30 karakter; ≤ 10;
  tekrarlar atılır.
- Not ≤ 1000 karakter, kırpılır.

## `ConversationSearchService` (`IConversationSearchPort`)

Yönetici konuşma araması. Tasarım:
[2026-10-05-conversation-search-design.md](../../../superpowers/specs/2026-10-05-conversation-search-design.md).

- Doğrulama: metin 2–200 karakter; başlangıç ≤ bitiş. Etiket kapanıştaki kuralla normalleştirilir.
- Sayfa 25; depodan bir fazlası istenir → `HasMore`.
- Alıntı: eşleşmenin çevresinden en fazla 160 karakter, kesilen uçlara "…", vurgu konumu
  (`HighlightStart/Length`). Metin aranmadıysa ilk müşteri mesajı.
- Depo (`IConversationSearchStore` / `PostgresConversationSearchStore`) tek SQL sorgusu çalıştırır.

## `TurkishText`

- `Fold`: arama katlaması — `İ I ı → i`, sonra Türkçe küçük harf; uzunluk korunur (vurgu konumu özgün
  metinde geçerli). SQL aynısını `lower(translate(text, 'İIıÇĞÖŞÜ', 'iiiçğöşü'))` ile yapar: `lower()`
  veritabanının yerel ayarına bağlıdır ('C' yerelinde yalnız ASCII), Türkçe büyük harfler bu yüzden açıkça
  çevrilir. "ı" ile "i" bilinçli olarak aynı sayılır.
- `NormalizeTag`: kapanış etiketleri.

## `ConversationClosingOptions` (`ConversationClosing`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `RequireReason` | `true` | Panelden kapatırken neden zorunlu. |
| `Reasons` | boş → `DefaultReasons` | `[{ Code, Label }]`. Yazılırsa varsayılanların yerine geçer. |

Varsayılanlar `DefaultReasons`'ta, `Reasons` listesinde değil: yapılandırma bağlayıcısı listeleri mevcut
öğelerin üstüne ekler — varsayılanlar listede dursaydı appsettings'teki nedenler onların sonuna eklenirdi.
