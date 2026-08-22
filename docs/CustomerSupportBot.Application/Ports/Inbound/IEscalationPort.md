# IEscalationPort

**Dosya:** `Ports/Inbound/IEscalationPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

İnsan temsilciye devir (eskalasyon) kayıtlarının yönetimi için primary port — oluşturma, listeleme, admin/agent kararı ve event bildirimleri.

## 2. Hangi amaçla kullanılır?

Bot bir konuşmayı insan müdahalesine ihtiyaç duyduğunda (`HumanHandoffAgent` gibi) yeni bir eskalasyon oluşturmak için; admin/agent panelinin açık eskalasyonları görüp karar vermesi (`acknowledge`/`resolve`/`dismiss`) için kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Eskalasyon CRUD'u, agent'a özel filtrelenmiş liste, karar uygulama, event yayınlama (`RequestCreated`/`RequestDecided`).
- **Üstlenmediği:** Hangi agent'ın hangi eskalasyona atanacağına dair iş kuralları — reroute mantığı `IHumanAgentPort.RerouteEscalation`'dadır.

## 4. Diğer katman/bileşenlerle ilişkileri

- `HumanHandoffAgent` (Adapters.Agents) yeni eskalasyon oluşturmak için bu portu çağırır.
- Api katmanındaki admin/agent endpoint'leri açık eskalasyonları listeler ve karar verir.
- `RequestCreated`/`RequestDecided` event'lerini `HitlEventPortService` dinleyip SSE/WebSocket üzerinden ilgili tarafa iletir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`GetRecentForAgentAsync`'in XML yorumundaki uyarı, ciddi bir performans/doğruluk kararını belgeler:

> ⚠️ **Daraltma ve limit birlikte, veri kaynağında uygulanır.** Önce son N kaydı alıp sonra elemek yanlış sonuç verir: o N kaydın tamamı başka agent'lara aitse liste boş döner, oysa daha gerisinde çağıranın kendi kaydı vardır. Aynı sebeple cache üzerinden de cevaplanamaz — cache'in kendisi zaten bir "son N" penceresidir.

Event tabanlı bildirim (`RequestCreated`/`RequestDecided`) tercih edilmiştir çünkü admin panelinin polling yapmadan anlık güncellenmesi gerekir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `EscalationRequest Create(EscalationRequest request)` | Yeni eskalasyon kaydı oluşturur. |
| `IReadOnlyList<EscalationRequest> GetOpen()` | Açık eskalasyonlar. |
| `IReadOnlyList<EscalationRequest> GetRecent(int count = 50)` | Son N eskalasyon (tümü). |
| `Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(string agentId, int count = 50, CancellationToken ct = default)` | Bir agent'ın görebileceği son N eskalasyon: atanmamış veya ona atanmış olanlar; daraltma veri kaynağında yapılır. |
| `EscalationRequest? Get(string id)` | Tek eskalasyon kaydı. |
| `bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)` | Admin kararı: `"acknowledge"`, `"resolve"`, `"dismiss"`. |
| `event EventHandler<EscalationRequest>? RequestCreated` | Yeni eskalasyon oluştuğunda tetiklenir. |
| `event EventHandler<EscalationRequest>? RequestDecided` | Bir karar uygulandığında tetiklenir. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.EscalationRequest`.

## Bağlantılar

- [IHumanAgentPort](IHumanAgentPort.md) — agent atama/reroute.
- [IHitlEventPort](IHitlEventPort.md) — event'lerin SSE/WebSocket'e iletilmesi.
