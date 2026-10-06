# VoiceCallEndpoints

- **Kaynak:** `CustomerSupportBot.Api/Endpoints/VoiceCallEndpoints.cs`
- **Tür:** `public static class` (`MapStaffVoiceCallEndpoints`, `MapCustomerVoiceCallEndpoints`)

## Personel (temsilci kapsamı)

Temsilci kimliği `linked_agent_id` talebinden, yoksa `user:{id}`; görünen ad `IHumanAgentPort`'tan.

| Uç | Açıklama |
|---|---|
| `POST /chat-sessions/{sid}/voice-calls` | Arama başlat. `409 voice_call_busy`, `409 not_in_human_mode`. |
| `GET /chat-sessions/{sid}/voice-calls` | Oturumun görüşmeleri. |
| `GET /voice-calls/mine` | Temsilcinin açık görüşmesi (yoksa 204). |
| `POST /voice-calls/{id}/signal` | offer / ice → müşteriye. |
| `POST /voice-calls/{id}/hangup?reason=` | İptal / bitir. |
| `POST /voice-calls/{id}/chunks?track=&seq=&offsetMs=&durationMs=` | Kayıt parçası (gövde: `audio/webm`). |
| `GET /voice-calls/{id}` | Görüşme + döküm satırları (oynatıcı). |
| `GET /voice-calls/{id}/chunks/{chunkId}` | Parça sesi. |
| `GET /voice-calls/{id}/ice-config` | STUN/TURN listesi. |

## Müşteri (`/chat/voice-calls`, `Customer` politikası, `general` hız sınırı)

| Uç | Açıklama |
|---|---|
| `POST /{id}/accept` | Rıza ile kabul. |
| `POST /{id}/decline?reason=` | Ret (`no_microphone` dahil). |
| `POST /{id}/signal` | answer / ice → temsilciye. |
| `POST /{id}/hangup?reason=` | Bitir. |
| `GET /{id}/ice-config` | STUN/TURN listesi. |

Sinyaller SSE'de `voice_signal` olayıyla gelir: müşteri `/chat/events/{id}`, temsilci canlı sohbet
aboneliği. Döküm satırları `bridge_message` olarak yalnızca personel tarafına gider.
