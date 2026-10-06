# VoiceCallService

- **Kaynak:** `CustomerSupportBot.Application/Services/Voice/VoiceCallService.cs`
- **Tür:** `public sealed class : IVoiceCallPort` (singleton)
- **Ayar:** `VoiceCall` bölümü (`VoiceCallOptions`)

## Ne işe yarar?

Sesli görüşmenin yaşam döngüsünü yönetir. Sesi taşımaz; yalnızca durum, sinyal aktarımı, kayıt
parçalarının kabulü ve zaman aşımlarıyla ilgilenir.

| Üye | Açıklama |
|---|---|
| `StartAsync` | `Enabled` değilse `disabled`; sohbet insan modunda değilse `not_in_human_mode`; temsilcinin ya da oturumun açık görüşmesi varsa `voice_call_busy` (tekil indeks, `TryCreateAsync` false). Müşteriye `ring`. |
| `AcceptAsync` / `DeclineAsync` | Yalnızca oturumun sahibi müşteri. Kabul rızayı (`ConsentAt`) yazar; temsilciye `accepted` / `declined`. |
| `HangupByStaffAsync` / `HangupByCustomerAsync` | Çalarken iptal, aktifken bitiş. Diğer tarafa `ended`; aktif görüşmeyse geçmişe süre notu. |
| `SignalFromStaffAsync` / `SignalFromCustomerAsync` | Yalnızca `offer`, `answer`, `ice` türleri ve eşleşen `callId` karşı tarafa aktarılır. |
| `UploadChunkAsync` | Yalnızca görüşmenin temsilcisi; `Active` ya da bitişten sonra `LateChunkGraceSeconds` içinde (rıza alınmışsa). `MaxChunkBytes` üstü `too_large`. |
| `GetViewAsync` / `GetChunkAudioAsync` | Görüşmenin temsilcisi ya da admin. |
| `GetIceConfigForStaffAsync` / `…ForCustomerAsync` | STUN + (tanımlıysa) TURN; TURN parolası 10 dk geçerli. |
| `GetAgentsInCallAsync` | Açık görüşmesi olan temsilciler — `/agents/presence` "Görüşmede" bilgisi. |
| `SweepAsync` | `RingTimeoutSeconds` dolan çalma → `Missed`; `ChunkStaleSeconds` boyunca parça gelmeyen aktif görüşme → `Failed` (temsilci sekmesi kapandı). |

Tüm geçişler koşullu güncellemedir (`TryUpdateAsync(call, expectedStatus)`): iki istek yarışırsa biri
`invalid_state` alır, kayıt bozulmaz.
