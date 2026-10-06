# VoiceCall

- **Kaynak:** `CustomerSupportBot.Domain/Model/Voice/VoiceCall.cs`
- **Tür:** `public sealed class` (+ `VoiceCallStatus`, `VoiceCallEndReasons`, `VoiceCallStateException`)
- **Tablo:** `voice.calls`

## Ne işe yarar?

Temsilci ile müşteri arasındaki tek bir sesli görüşmenin durumunu taşır. Durum geçişleri yalnızca
sınıfın metotlarıyla yapılır; geçersiz geçiş `VoiceCallStateException` fırlatır.

| Metot | Geçiş |
|---|---|
| `Start` | Yeni görüşme, `Ringing`. |
| `Accept` | `Ringing → Active`; `ConsentAt` (müşteri rızası) ve `AnsweredAt` yazılır. |
| `Decline` | `Ringing → Declined` (`declined` ya da `no_microphone`). |
| `Miss` | `Ringing → Missed` (çalma süresi doldu). |
| `Hangup` | `Ringing → Cancelled`; `Active → Ended`, ya da sebep `connection_lost` / `connect_failed` ise `Failed`. |

`IsOpen` (`Ringing` ya da `Active`) ve `Duration` (cevaplanma → bitiş) türetilmiş özelliklerdir.

## Tasarım notu

"Temsilci başına tek açık görüşme" kuralı sınıfta değil veritabanındadır:
`ux_calls_agent_open` ve `ux_calls_session_open` kısmi tekil indeksleri (`status IN ('Ringing','Active')`).
Böylece iki pod aynı anda arama başlatsa da yalnızca biri kazanır.
