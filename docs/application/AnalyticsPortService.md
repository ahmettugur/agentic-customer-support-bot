# AnalyticsPortService

**Dosya:** `Services/AnalyticsPortService.cs`  
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

---

## Metodlar

### `RateAsync`

```csharp
Task RateAsync(string sessionId, int stars, string? feedback, CancellationToken ct = default)
```

**Validasyon:** `stars` 1–5 aralığında olmalı, aksi hâlde `ArgumentException` fırlatır.

`IRatingStore.Upsert` çağırır — aynı session için tekrar puanlama öncekinin üzerine yazar.

---

### `GetRatingAsync / GetRecentRatingsAsync / GetAllRatingsAsync`

| Metod | Açıklama |
|-------|---------|
| `GetRatingAsync(sessionId)` | Belirli session'ın puanı; yoksa `null` |
| `GetRecentRatingsAsync(count)` | Son N puanı döner |
| `GetAllRatingsAsync()` | Tüm puanları döner |

---

### `GetSummaryAsync`

```csharp
Task<RatingSummary> GetSummaryAsync(CancellationToken ct = default)
```

`IRatingStore.GetSummary()` sonucunu döner:

| Alan | Açıklama |
|------|---------|
| `Count` | Toplam puanlanan oturum sayısı |
| `Average` | Ortalama puan (0.0–5.0) |
| `Distribution` | Her yıldız seviyesinin dağılımı (1→N, 2→N, ...) |

---

### `GetDashboardAsync`

```csharp
Task<AnalyticsDashboard> GetDashboardAsync(CancellationToken ct = default)
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

### `GetSessionAnalyticsAsync`

```csharp
Task<SessionAnalytics?> GetSessionAnalyticsAsync(string sessionId, CancellationToken ct = default)
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
POST /analytics/rate          → RateAsync
GET  /analytics/summary       → GetSummaryAsync
GET  /analytics/dashboard     → GetDashboardAsync
GET  /analytics/{sessionId}   → GetSessionAnalyticsAsync
```
