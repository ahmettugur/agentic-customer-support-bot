# Analytics — Analitik ve Raporlama

Bu doküman admin analytics dashboard'unu, rating sistemini ve veri toplama mekanizmalarını anlatır.

---

## 1. Genel Bakış

`AnalyticsService` oturum istatistikleri, intent dağılımı, kullanıcı puanlamaları ve performans metriklerini toplar. Admin paneli "Analytics" sekmesinden erişilir.

---

## 2. API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/analytics/dashboard` | `GET` | Genel analitik özeti |
| `/analytics/ratings/recent?count=N` | `GET` | Son N kullanıcı puanlaması |

### Dashboard Yanıt Formatı

```json
{
  "totalSessions": 42,
  "totalMessages": 186,
  "intentDistribution": {
    "sipariş_sorgulama": 15,
    "ürün_bilgisi": 12,
    "sipariş_oluşturma": 8,
    "şikayet": 5,
    "genel": 2
  },
  "averageRating": 4.2,
  "totalRatings": 28,
  "ratingDistribution": {
    "5": 14,
    "4": 8,
    "3": 4,
    "2": 1,
    "1": 1
  }
}
```

---

## 3. Rating Sistemi

### Kullanıcı Puanlama Akışı

```
Sohbet tamamlanır (Bot moduna dönüş veya yeterli mesaj sayısı)
    │
    ├─ messageCount >= 4
    ├─ ratingShown == false
    └─ humanModeActive == false
    │
    ▼
Puanlama widget'ı gösterilir (1-5 yıldız + opsiyonel yorum)
    │
    ▼
POST /sessions/{sessionId}/rating
    body: { "stars": 4, "comment": "Hızlı çözüm" }
    │
    ▼
IRatingStore.SaveAsync(rating)
```

### Rating Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `POST /sessions/{sessionId}/rating` | `POST` | Puanlama kaydet |
| `GET /sessions/{sessionId}/rating` | `GET` | Oturumun puanını sorgula |

### Rating Entity

```json
{
  "id": "guid",
  "sessionId": "s123",
  "stars": 4,
  "comment": "Hızlı çözüm",
  "createdAt": "2026-05-08T10:30:00Z"
}
```

---

## 4. Self-Improving Loop Entegrasyonu

Rating verileri `LessonMiner` tarafından kullanılır:

- `stars <= MinRatingForLesson` (default: 2) olan trace'ler "iyileştirme adayı" olarak seçilir
- Hatalı veya sanity-fail trace'ler de aday havuzuna eklenir
- `LessonMiner` bu trace'lerden LLM ile "lesson" çıkarır

Detay → [intelligence.md](intelligence.md).

---

## 5. Admin UI

`/admin.html` → "Analytics" sekmesi:

- **Genel istatistikler**: Toplam oturum, mesaj, ortalama puan
- **Intent dağılımı**: Bar chart veya liste
- **Son puanlamalar**: Yıldız, yorum, oturum bağlantısı
- **Puan dağılımı**: 1-5 yıldız bazlı dağılım

---

## Çapraz Referanslar

- **Self-Improving Loop** → [intelligence.md](intelligence.md)
- **API endpoint'leri** → [api.md](api.md)
- **Telemetri ve maliyet** → [telemetry.md](telemetry.md)
