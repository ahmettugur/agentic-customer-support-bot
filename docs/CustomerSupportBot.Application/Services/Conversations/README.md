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

## `ConversationClosingOptions` (`ConversationClosing`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `RequireReason` | `true` | Panelden kapatırken neden zorunlu. |
| `Reasons` | boş → `DefaultReasons` | `[{ Code, Label }]`. Yazılırsa varsayılanların yerine geçer. |

Varsayılanlar `DefaultReasons`'ta, `Reasons` listesinde değil: yapılandırma bağlayıcısı listeleri mevcut
öğelerin üstüne ekler — varsayılanlar listede dursaydı appsettings'teki nedenler onların sonuna eklenirdi.
