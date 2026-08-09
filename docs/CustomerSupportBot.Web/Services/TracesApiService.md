# TracesApiService

## Ne İşe Yarar
Reasoning trace oturumlarını ve detaylarını backend'den çeken HTTP istemci servisidir. Bridge chat geçmişi, approval ve escalation verilerini de oturum bazlı filtreleyerek sunar.

## Hangi Amaçla Kullanılır
`Traces.razor` ve `TraceDetailPanel.razor` bileşenlerinde trace oturumları listesi, tekil trace detayı ve replay verileri gösterilirken kullanılır.

## Sorumlulukları
- Trace oturumlarını listelemek (`/traces/sessions`).
- Tekil trace detayını çekmek (`/traces/{traceId}`).
- Oturum bazlı trace listesini çekmek (`/traces/by-session/{sessionId}`).
- Son N trace'i çekmek (`/traces/recent`).
- Bridge chat geçmişini çekmek (`/chat-sessions/{id}/history`).
- Approval ve escalation'ları oturum bazlı filtrelemek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient`.
- **Kullanan bileşenler**: `Pages/Traces.razor`, `Components/TraceDetailPanel.razor`, `Pages/Replay.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `TraceEndpoints`, `ChatEndpoints`.
- **Model bağımlılığı**: [TraceDetailModels](../Models/TraceDetailModels.md), [AdminModels](../Models/AdminModels.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Tüm okuma yollarında hata yutma deseni. `GetApprovalsBySessionAsync` ve `GetEscalationsBySessionAsync` sunucu tarafında filtreleme endpoint'i olmadığı için tüm son kayıtları çekip client-side LINQ ile filtreler.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetSessionsAsync()` | Trace oturumlarını listeler. |
| `GetTraceAsync(traceId)` | Tekil trace detayını çeker. |
| `GetBySessionAsync(sessionId)` | Oturuma ait tüm trace'leri çeker. |
| `GetRecentAsync(count)` | Son N trace'i çeker. |
| `GetBridgeHistoryAsync(sessionId, take)` | Human-mode bridge mesaj geçmişini çeker. |
| `GetApprovalsBySessionAsync(sessionId)` | Son 200 approval'dan oturuma ait olanları filtreler. |
| `GetEscalationsBySessionAsync(sessionId)` | Son 200 escalation'dan oturuma ait olanları filtreler. |

### İlişkili Record'lar (aynı dosyada)

| Record | Açıklama |
|--------|----------|
| `TraceSession` | Oturum özeti (sessionId, title, traceCount, messageCount, lastTraceAt). |
| `SessionChatMessage` | Basit chat mesajı (role, text). |
| `BridgeChatMessage` | Bridge mesajı (sender, text, humanAgent, timestamp). |

## Bağımlılıklar
- `HttpClient`
- [TraceDetailModels](../Models/TraceDetailModels.md), [AdminModels](../Models/AdminModels.md)
