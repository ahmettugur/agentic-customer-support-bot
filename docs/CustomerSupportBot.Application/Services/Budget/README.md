# CustomerSupportBot.Application.Services.Budget

LLM harcama limiti. Tasarım:
[docs/superpowers/specs/2026-10-05-llm-spend-cap-design.md](../../../superpowers/specs/2026-10-05-llm-spend-cap-design.md).
Yapılandırma: [operations.md → `LlmBudget`](../../../operations.md).

## `LlmSpendGuard` (`ILlmSpendGuard`)

| Üye | Davranış |
|---|---|
| `CheckAsync(sessionId)` | Aşılmış ilk limit: gün → ay → görüşme; yoksa ya da kapalıysa `null`. |
| `RecordAsync(cost, sessionId)` | Gün, ay ve (varsa) görüşme sayacını artırır; eşik geçilirse uyarır. Hata fırlatmaz. |
| `GetStatusAsync` | Panel: limitler ve bugünkü/aylık harcama. |

- **Sayaçlar** `ILlmSpendCounter` (Redis, `RedisLlmSpendCounter`): `llm-spend:day:yyyyMMdd` (TTL 2 gün),
  `llm-spend:month:yyyyMM` (40 gün), `llm-spend:session:{id}` (7 gün). Artış ve TTL tek Lua betiğinde.
- **Tohumlama:** gün/ay sayacı yoksa `ILlmCallPersistencePort.GetTotalCostSinceAsync` ile (`SET NX`) — Redis
  verisi kaybolursa dönem harcaması sıfırlanmaz.
- **Fail-open:** sayaç/veritabanı hatası loglanır, çağrı engellenmez.
- **Uyarılar:** artış eşiği geçtiğinde (önceki < eşik ≤ yeni); `INotificationLedger` anahtarı
  `llm-budget:{day|month}:{dönem}:{warning|limit}` → çok pod'da tek bildirim. E-posta gönderilemezse talep geri
  bırakılır.

## Uygulandığı yerler

- `SpendLimitChatClient` (Adapters.Telemetry): tüm LLM çağrılarının en dış sarmalayıcısı; aşımda
  `LlmBudgetExceededException`, yanıttan sonra maliyeti (beklemeden) kaydeder. Telemetriden bağımsızdır.
- `ChatPortService`: tur başında ön kontrol — aşımda LLM'e gidilmez, sabit yanıt (akışta
  `response_start/delta/complete`, `terminationReason = "budget_exceeded"`) ve geçmişe yazım. Görüşme
  limitinde oturum için açık eskalasyon yoksa bir tane açılır.
- `RealtimeEndpoints`: aşımda yeni sesli bağlantı 503.
