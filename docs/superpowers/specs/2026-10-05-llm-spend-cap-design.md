# LLM Harcama Limiti — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/llm-spend-cap`

## Amaç

LLM maliyeti ölçülüyor (görüşme başına maliyet panelde) ama sınırı yok: bir döngü, kötüye kullanım ya da
trafik patlaması faturayı sınırsız büyütebilir. Günlük/aylık bütçe ve görüşme başına tavan gerekli;
yaklaşınca uyarı, aşılınca kontrollü davranış.

Başarı ölçütleri:
- Günlük, aylık ve görüşme başına USD limiti `appsettings`'ten (`LlmBudget`); `0` = sınırsız.
- Limit aşıldığında yeni LLM çağrısı yapılmaz; müşteri anlaşılır bir mesaj alır (hata değil).
- Eşik (varsayılan %80) ve %100 aşımında bir kez uyarı: log + isteğe bağlı e-posta.
- Çok pod'lu kurulumda toplam tek: sayaçlar Redis'te.
- Panelde bugünkü/aylık harcama ve limit görünür.

## Yapılandırma (`LlmBudget`)

| Ayar | Sınıf varsayılanı | appsettings | Anlamı |
|---|---|---|---|
| `Enabled` | `false` | `true` | Kapalıyken hiçbir kontrol yapılmaz. |
| `DailyLimitUsd` | `0` | `50` | UTC günü. `0` = sınırsız. |
| `MonthlyLimitUsd` | `0` | `1000` | UTC ayı. |
| `PerConversationLimitUsd` | `0` | `1` | Bir görüşmeye atfedilen toplam. |
| `WarningThresholdPercent` | `80` | `80` | Günlük/aylık uyarı eşiği. |
| `AlertEmails` | `[]` | `[]` | Uyarı e-postası alıcıları (`Email:Enabled` gerekir). |

## Davranış

- **Yumuşak tavan:** kontrol çağrıdan önce, kayıt çağrıdan sonra (maliyet ancak yanıtla bilinir). Aynı anda
  süren çağrılar limiti biraz aşabilir.
- **Sohbet turu başında ön kontrol** (`ChatPortService`, yazılı ve akış): limit aşıldıysa LLM'e gidilmez,
  müşteriye sabit mesaj verilir ve geçmişe yazılır.
  - Günlük/aylık: "Otomatik asistan şu anda kullanılamıyor…" (eskalasyon açılmaz — herkes için açılırsa
    temsilci kuyruğu dolar).
  - Görüşme başına: "…talebiniz bir müşteri temsilcisine iletildi" ve oturum için açık eskalasyon yoksa
    bir eskalasyon açılır (kaçak döngü/kötüye kullanım insan gözüne gelir).
- **Tüm LLM çağrıları** (`SpendLimitChatClient`, telemetriden bağımsız sarmalayıcı): arka plan işleri
  (ders çıkarma, profil birleştirme) ve tur ortasında aşım dahil, limit aşıldıysa
  `LlmBudgetExceededException` fırlatılır.
- **Sesli görüşme:** limit aşıldıysa yeni bağlantı açılmaz (503). Realtime ses maliyeti bu sayaçlara
  girmez (OpenAI Realtime tokenları `IChatClient`'tan geçmiyor) — bilinen sınır.
- **Sayaçlar:** Redis `csbot:llm-spend:{day|month|session}:…` (`INCRBYFLOAT` + ilk yazımda TTL, tek Lua
  betiği). Gün/ay sayacı yoksa (Redis yeniden başladı) `llm_call_usage` toplamıyla tohumlanır (`SET NX`).
- **Redis/sayaç hatası:** açık kalır (fail-open) ve uyarı loglanır — bütçe kontrolü hizmeti düşürmemeli.
- **Uyarılar:** bir artış eşiği geçtiğinde (önceki < eşik ≤ yeni) `INotificationLedger` ile tek pod'da bir kez
  (`llm-budget:{dönem}:{seviye}`); e-posta gönderilemezse talep geri bırakılır.

## Mimari

- Application: `LlmBudgetOptions`, `ILlmSpendGuard` / `LlmSpendGuard` (kontrol, kayıt, uyarı, durum),
  `ILlmSpendCounter` (outbound), `ILlmCallPersistencePort.GetTotalCostSinceAsync` (tohumlama).
- Adapters: `RedisLlmSpendCounter`, `SpendLimitChatClient` (Telemetry), Postgres toplam sorgusu.
- Analitik: `AnalyticsDashboard` → `LlmBudgetEnabled`, `DailyLlmLimitUsd/SpentUsd`,
  `MonthlyLlmLimitUsd/SpentUsd`; panelde kart.

## Test

- Guard: kapalı, her kapsam, sınırsız (0), tohumlama, fail-open, eşik uyarısı bir kez, e-posta ve geri
  bırakma, durum.
- `SpendLimitChatClient`: aşımda çağrı yapılmaz; maliyet kaydı (yanıt ve akış); görüşme kimliği.
- Sohbet: ön kontrolde LLM çağrılmaz, mesaj + geçmiş; görüşme kapsamında tek eskalasyon; akış olayları.
- Redis (Testcontainers): artış, TTL, tohumlama yalnız yoksa. Postgres: dönem toplamı.
- Sesli uç: limit aşıldıysa 503. Panel: sözleşme testi ve tarayıcı.
