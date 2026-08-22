# ISessionPort

**Dosya:** `Ports/Inbound/ISessionPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Oturum (session) yaşam döngüsü ve konuşma geçmişi için primary port — oturum oluşturma/okuma/güncelleme, geçmiş sorgulama, mesaj ekleme, durum çıkarımı ve durum mutasyonu.

## 2. Hangi amaçla kullanılır?

`ChatPortService` her chat turunda bu portu kullanarak oturumu bulur/oluşturur, geçmişe yeni bir değişim (exchange) ekler ve oturum durumunu (ör. çıkarılan müşteri/sipariş kimlikleri) günceller.

## 3. Sorumlulukları

- **Üstlendiği:** Oturumun tüm yaşam döngüsü işlemlerini tek bir sözleşme altında sunmak.
- **Üstlenmediği:** Oturumun nasıl saklandığı (Postgres+Redis cross-pod sync, bellek içi vb.) — implementasyon (`ChatPortService`'in kullandığı `ISessionManager` Outbound port'u) bunu saklar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu, Outbound `ISessionManager`/`IChatHistoryStore` gibi persistence portlarını sarar.
- `ChatPortService` başlıca tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`GetAllSessionsAsync(string? forCustomerId)` parametresinin opsiyonel olması, aynı metodun hem admin/agent (tüm oturumlar) hem müşteri (yalnızca kendi oturumları) senaryosuna hizmet etmesini sağlar — iki ayrı metot yerine tek, güvenlik açısından çağıranın sorumluluğunda olan bir parametre tercih edilmiştir.

`MutateStateAsync(string sessionId, Action<SessionState> mutator, ...)` deseni, oturum durumunu güncellemenin "oku-değiştir-yaz" adımlarını tek bir atomik işlemde saklar — çağıranın ham `SessionState`'i alıp elle geri yazmasını (ve olası kayıp güncellemeleri) önler.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<AgentSession> GetOrCreateSessionAsync(string? sessionId, CancellationToken ct = default)` | Oturumu bulur, yoksa oluşturur. |
| `Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)` | Oturumu okur; yoksa `null`. |
| `Task UpdateSessionAsync(AgentSession session, CancellationToken ct = default)` | Oturumu kaydeder. |
| `Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(string? forCustomerId = null, CancellationToken ct = default)` | Oturum listesi; `forCustomerId` verilirse yalnızca o müşterininkiler (müşteri rolü için), `null` ise hepsi (yalnızca admin/agent için uygun). |
| `Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)` | Konuşma geçmişini döner. |
| `Task AddExchangeAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)` | Geçmişe bir kullanıcı+bot değişimi ekler. |
| `Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)` | Mesajlardan kimlik/duygu gibi bilgileri çıkarıp durumu günceller. |
| `Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)` | Durumu atomik olarak oku-değiştir-yaz döngüsüyle günceller. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Ports.Outbound.Persistence` (implementasyon aracılığıyla), `CustomerSupportBot.Domain.Model` (`AgentSession`, `SessionState`, `ConversationMessage`).

## Bağlantılar

- [IChatSessionPort](IChatSessionPort.md) — admin/agent tarafındaki canlı oturum yönetimi, bu porttan farklı bir görünüm.
