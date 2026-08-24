# AdminApiService

## Ne İşe Yarar
Admin ve Agent panelinin backend API'sine HTTP istekleri gönderen servis katmanıdır. Approvals, escalations, chat sessions, agents, improvements ve sentiment endpoint'lerini sarar.

## Hangi Amaçla Kullanılır
`Admin.razor` sayfasındaki tüm veri çekme ve aksiyon tetikleme işlemlerinde kullanılır. JavaScript'teki `admin.js` dosyasının C# karşılığıdır.

## Sorumlulukları
- Kullanıcının rolüne göre endpoint prefix'i belirlemek (`/agent` veya boş).
- Approval CRUD: bekleyen/son onayları listelemek, onaylamak, reddetmek.
- Escalation yönetimi: açık/son escalation'ları listelemek, acknowledge, resolve, dismiss, replan.
- Chat session yönetimi: aktif oturumları listelemek, geçmişi çekmek, takeover/release, mesaj göndermek, replan.
- Agent ve session listelerini çekmek.
- Sentiment analizi sonuçlarını almak.
- Improvement (lesson) yönetimi: mine, approve, reject.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient`, [AppAuthStateProvider](AppAuthStateProvider.md).
- **Kullanan bileşen**: `Pages/Admin.razor` (ve `Admin.razor.cs` code-behind).
- **Backend karşılığı**: `CustomerSupportBot.Api` → `AdminEndpoints`, `AgentsEndpoints`, `ImprovementsEndpoints` vb.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Role-aware prefix (`PrefixAsync()`) sayesinde aynı servis hem Admin hem Agent rolü için çalışır. Okuma yollarında hata yutularak boş liste döndürülür (UI çökmez); yazma yollarında `EnsureSuccessStatusCode()` ile hata fırlatılır.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `PrefixAsync()` | Kullanıcı rolünden endpoint prefix'i üretir. |
| `GetPendingApprovalsAsync()` | Bekleyen onay isteklerini listeler. |
| `GetRecentApprovalsAsync(count)` | Son N onay kaydını listeler. |
| `ApproveAsync(id, reason, decidedBy)` | Bir onay isteğini onaylar. |
| `RejectAsync(id, reason, decidedBy)` | Bir onay isteğini reddeder. |
| `GetOpenEscalationsAsync()` | Açık escalation'ları listeler. |
| `GetRecentEscalationsAsync(count)` | Son N escalation'ı listeler. |
| `AcknowledgeAsync(id, assignedTo)` | Escalation'ı sahiplenir. |
| `ResolveEscalationAsync(id, resolution, assignedTo)` | Escalation'ı çözer. |
| `DismissAsync(id, resolution)` | Escalation'ı reddeder. |
| `ReplanEscalationAsync(id, note, requestedBy)` | Escalation için yeniden planlama tetikler. |
| `GetActiveChatsAsync()` | Human-mode'daki aktif chat oturumlarını listeler. |
| `GetChatHistoryAsync(sessionId, take)` | Oturum mesaj geçmişini çeker. |
| `TakeoverAsync(sessionId, humanAgent)` | Oturumu insan agent'a devreder. |
| `ReleaseAsync(sessionId)` | Human-mode oturumunu AI'ya geri bırakır. |
| `SendChatMessageAsync(sessionId, text)` | Human-mode'da mesaj gönderir. |
| `ReplanChatAsync(sessionId, note, requestedBy)` | Chat oturumu için yeniden planlama tetikler. |
| `GetAgentsAsync()` | Kayıtlı agent listesini çeker. |
| `GetSessionsAsync()` | Tüm oturum özetlerini listeler. |
| `GetSentimentAsync(sessionId)` | Oturumun sentiment analizini çeker. |
| `GetLessonsAsync(status)` | Belirli durumdaki improvement/lesson tekliflerini listeler. |
| `MineImprovementsAsync()` | Improvement taramasını tetikler; (candidates, proposed, error) döner. |
| `ApproveLessonAsync(id, reason)` | Bir lesson teklifini onaylar. |
| `RejectLessonAsync(id, reason)` | Bir lesson teklifini reddeder. |

## Yardımcı Tip: `ChatSentiment`

Bu dosyada (`AdminApiService.cs`) tanımlı, `AdminModels.cs`'e dahil edilmemiş küçük bir record:

```csharp
public sealed record ChatSentiment(string? Sentiment, double Score);
```

`GetSentimentAsync`'in dönüş tipidir — `/chat-sessions/{sessionId}/sentiment` endpoint'inin yanıtını taşır (`Sentiment`: etiket, ör. "positive"/"negative"/"neutral"; `Score`: sayısal skor). `AdminModels.cs`'teki diğer DTO'lardan ayrı tutulmasının nedeni yok gibi görünüyor — muhtemelen bu metot sonradan eklendiği için buraya kondu; işlevsel bir fark yok, ikisi de aynı şekilde deserialize edilen düz DTO'lar.

## Bağımlılıklar
- `HttpClient` — `AuthorizedHttpClientHandler` zincirli.
- [AppAuthStateProvider](AppAuthStateProvider.md) — Rol tespiti için.
- [AdminModels](../Models/AdminModels.md) — DTO tanımları (`ApprovalRequest`, `EscalationRequest` vb.).
