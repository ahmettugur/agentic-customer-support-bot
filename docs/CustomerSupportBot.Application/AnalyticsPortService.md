# AnalyticsPortService

**Dosya:** `Services/Telemetry/AnalyticsPortService.cs`  
**Implements:** `IAnalyticsPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Oturum puanlarını (1-5 yıldız) kaydeder ve admin dashboard için analitik verilerini toplar. 4 farklı driven port'tan veri çekerek kapsamlı özet üretir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IRatingStore` | Oturum puanları deposu |
| `ISessionManager` | Tüm session'ların listesi |
| `IApprovalQueue` | HITL onay sayıları |
| `IEscalationSink` | Eskalasyon sayıları |
| `ILogger<AnalyticsPortService>` | Loglama |

---

## Metodlar

Tüm metodlar **senkrondur** (`Task` yok, `CancellationToken` almazlar).

### `Rate`

```csharp
ConversationRating Rate(string sessionId, int stars, string? comment = null)
```

**Validasyon:** `stars` 1–5 aralığında olmalı, aksi hâlde `ArgumentOutOfRangeException` fırlatır.

`IRatingStore.Upsert` çağırır — aynı session için tekrar puanlama öncekinin üzerine yazar. Oluşturulan/güncellenen `ConversationRating` kaydını döner.

---

### `GetRating / GetRecentRatings / GetAllRatings`

| Metod | Açıklama |
|-------|---------|
| `GetRating(sessionId)` | Belirli session'ın puanı; yoksa `null` |
| `GetRecentRatings(count = 20)` | Son N puanı döner |
| `GetAllRatings()` | Tüm puanları döner |

---

### `GetSummary`

```csharp
object GetSummary()
```

`IRatingStore.GetSummary()` çağrılmaz — `_ratings`, `_sessions`, `_approvals`, `_escalations` üzerinden doğrudan bir anonim nesne inşa edilir:

| Alan | Açıklama |
|------|---------|
| `Count` | Toplam puanlanan oturum sayısı |
| `Average` | Ortalama puan (0.0–5.0) |
| `Distribution` | Her yıldız seviyesinin dağılımı (1→N, 2→N, ...) |

---

### `GetDashboard`

```csharp
AnalyticsDashboard GetDashboard()
```

Dört ayrı kaynaktan veri çekerek kapsamlı dashboard üretir:

```
IRatingStore      → rating özeti + dağılım
ISessionManager   → tüm session'lar
IApprovalQueue    → pending + son 50 onay
IEscalationSink   → açık + son 50 eskalasyon
```

**`AnalyticsDashboard` alanları:**

| Alan | Kaynak | Açıklama |
|------|-------|---------|
| `TotalSessions` | SessionManager | Toplam oturum sayısı |
| `ActiveSessions` | SessionManager | Aktif (son 30dk) oturumlar |
| `RatingSummary` | RatingStore | Ortalama puan ve dağılım |
| `PendingApprovals` | ApprovalQueue | Bekleyen onay sayısı |
| `TotalApprovals` | ApprovalQueue | Son 50'deki toplam onay |
| `OpenEscalations` | EscalationSink | Açık eskalasyon sayısı |
| `TotalEscalations` | EscalationSink | Son 50'deki toplam eskalasyon |
| `IntentDistribution` | SessionManager | Intent başına oturum sayısı |
| `PhaseDistribution` | SessionManager | Phase başına sayım |
| `SentimentDistribution` | SessionManager | Sentiment başına sayım |
| `ConsecutiveNegativeCounts` | SessionManager | Negatif sentiment serisi dağılımı |

---

### `GetSessionAnalytics`

```csharp
SessionAnalytics? GetSessionAnalytics(string sessionId)
```

Belirli bir oturum için tam analitik döner:
- Session durumu ve metadata
- Konuşma geçmişi özeti
- Bekleyen onaylar
- Açık eskalasyonlar
- Rating bilgisi

---

## API endpoint'leri

```http
POST /sessions/{sid}/rating   → Rate
GET  /sessions/{sid}/rating   → GetRating
GET  /analytics/ratings/recent → GetRecentRatings
GET  /analytics/dashboard     → GetDashboard
GET  /analytics/session/{sid} → GetSessionAnalytics
```
