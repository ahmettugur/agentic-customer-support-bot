# ChatApiService

## Ne İşe Yarar
Müşteri chat sayfasının backend API'sine HTTP istekleri gönderen servis katmanıdır. Rating, oturum, mesaj geçmişi ve approval bildirim endpoint'lerini sarar.

## Hangi Amaçla Kullanılır
`Chat.razor` sayfasında kullanıcı oturumlarını listeleme, mesaj geçmişini görüntüleme, yıldız değerlendirmesi gönderme ve approval bildirimlerini yönetme işlemlerinde kullanılır.

## Sorumlulukları
- Rating gönderme ve okuma (`/sessions/{id}/rating`).
- Oturum listesi çekme (`/sessions/`).
- Oturum mesaj geçmişi çekme.
- Görülmemiş approval bildirimlerini çekme ve "görüldü" işaretleme.
- Müşterinin approval geçmişini çekme.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient` (Bearer token zincirli).
- **Kullanan bileşen**: `Pages/Chat.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `ChatEndpoints`, `SessionEndpoints`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Okuma yollarında hata yutma (exception-swallowing) deseni; approval "seen" markalaması kritik olmadığından hata yutulur.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `SubmitRatingAsync(sessionId, stars, feedback)` | Oturuma yıldız değerlendirmesi gönderir. |
| `GetRatingAsync(sessionId)` | Mevcut rating'i çeker. |
| `GetSessionsAsync()` | Tüm oturumları listeler. |
| `GetSessionMessagesAsync(sessionId)` | Oturumun mesaj geçmişini çeker. |
| `GetUnseenApprovalsAsync(sessionId)` | Müşterinin henüz görmediği approval kararlarını çeker. |
| `MarkApprovalSeenAsync(sessionId, approvalId)` | Approval bildirimini "görüldü" işaretler. |
| `GetApprovalHistoryAsync()` | Müşterinin tüm approval geçmişini çeker. |

### İlişkili Record'lar (aynı dosyada tanımlı)

| Record | Açıklama |
|--------|----------|
| `RatingResponse` | Rating yanıtı (stars, feedback). |
| `SessionInfo` | Oturum özeti (id, lastActivity, messageCount). |
| `SessionMessage` | Tek mesaj (role, content, timestamp). |
| `UnseenApproval` | Görülmemiş approval (id, toolName, status, decisionReason, executionResult, decidedAt). |
| `ApprovalHistoryItem` | Approval geçmiş kaydı. |

## Bağımlılıklar
- `HttpClient` — Bearer token zincirli.
