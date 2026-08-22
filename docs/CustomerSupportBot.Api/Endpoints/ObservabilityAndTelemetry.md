# Gözlemlenebilirlik ve Yönetim Uç Noktaları

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/AgentsEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/AnalyticsEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/EvaluationEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/SlaEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/TelemetryEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/TraceEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

## 1. Ne İşe Yarar

Admin paneli için "operasyonel görünürlük" sağlayan altı ayrı endpoint sınıfını bir araya
getirir: insan temsilci kaydı (Smart Routing), analitik dashboard + konuşma değerlendirmesi,
otomatik değerlendirme (evaluation) senaryolarının koşturulması, SLA ihlali izleme, model
maliyet/token özeti ve akıl yürütme (reasoning) trace'lerinin incelenmesi. Bunların hiçbiri
sohbet akışının kendisini etkilemez — hepsi **izleme/yönetim** amaçlıdır.

## 2. Hangi Amaçla Kullanılır

- **AgentsEndpoints:** Admin panelinde insan temsilci CRUD'u ve manuel eskalasyon yönlendirme
  (reroute) — bir eskalasyon yanlış/aşırı yüklü bir temsilciye atandıysa elle değiştirme.
- **AnalyticsEndpoints:** Admin dashboard'daki genel istatistikler + kullanıcıların (oturum
  açmadan) konuşma sonunda bıraktığı 1-5 yıldız değerlendirme.
- **EvaluationEndpoints:** `docs/evaluation-scenarios.yaml`'daki senaryoları HTTP üzerinden
  tetiklemek için (regresyon/kalite testi amaçlı, CI'da veya elle çağrılır).
- **SlaEndpoints:** SLA Guardian arka plan servisinin ürettiği ihlal/uyarı olaylarını ve güncel
  kuyruk durumunu göstermek için.
- **TelemetryEndpoints:** Model bazlı token + USD maliyet özetini admin dashboard'da kart olarak
  göstermek için.
- **TraceEndpoints:** Bir sohbet turunda modelin hangi bağlamı gördüğünü, hangi tool'ları
  çağırdığını (`ReasoningTrace`) incelemek için — hatalı yanıtların kök nedenini bulmada kritik.

## 3. Sorumlulukları

- Her sınıf yalnızca kendi ilgili `I*Port` (Application katmanı, Inbound) arayüzüne HTTP
  yüzeyi sağlar; iş mantığını barındırmaz.
- `EvaluationEndpoints` ayrıca `evaluation-scenarios.yaml` dosya yolunu çözme sorumluluğunu taşır
  (`ResolveScenarioPath`) — iki olası kök dizini (`ContentRootPath/docs`,
  `ContentRootPath/../docs`) dener, farklı çalıştırma bağlamlarına (IDE'den, yayınlanmış binary'den)
  dayanıklı olmak için.
- **Üstlenmediği:** rating/analytics/trace/telemetry verisinin nasıl toplandığı/saklandığı — bu
  Application/Adapters katmanlarındaki port implementasyonlarının işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IHumanAgentPort`, `IAnalyticsPort`, `IEvaluationPort`, `ISlaPort`, `ITelemetryPort`,
  `ITracePort` — Application katmanı Inbound port'ları (`CustomerSupportBot.Application/Ports/Inbound/`).
- [ScenarioLoader](../Infrastructure/ScenarioLoader.md) — YAML senaryo dosyasını `EvaluationScenarioFile`'a çözümleyen yardımcı sınıf.
- [SlaGuardianService](../Workers/SlaGuardianService.md) — `SlaEndpoints`'in okuduğu olayları üreten arka plan servisi.
- [TelemetryChatClient](../../CustomerSupportBot.Adapters.Telemetry/Chat/TelemetryChatClient.md) — `ITelemetryPort.GetCostSnapshot()`'ın kaynaklandığı asıl maliyet toplayıcı.
- `Program.cs` — `AnalyticsEndpoints`'teki dashboard/session/ratings-recent uçları
  `RequireAuthorization("Admin")` ile korunur; rating gönderme/okuma uçları herkese açıktır
  (kullanıcı oturum açmadan puan bırakabilmeli) ama `general` rate-limit policy'sine tabidir.
  Diğer beş sınıf, `Program.cs`'teki genel `adminScope`/`agentScope` gruplarına dahil edilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Altı küçük sınıfa bölünmüş, tek dev bir "AdminEndpoints" yapılmamış** — her biri farklı bir
  Application port'una karşılık geldiğinden, tek dosyada birleştirmek ilgisiz endpoint gruplarını
  aynı yerde tutup gezinmeyi zorlaştırırdı (Single Responsibility, dosya seviyesinde).
  Dokümantasyonda tek dosyada toplanmaları yalnızca "hepsi izleme amaçlı" ortak temaları
  yüzünden, kod tarafında hâlâ ayrıdır.
- **Rating uçları kasıtlı olarak public (auth gerektirmez):** bir müşteri konuşma bitince oturum
  açmadan puan bırakabilmeli — zorunlu login, geri bildirim oranını düşürürdü. Buna karşılık
  `general` rate limit'e tabidir (kötüye kullanımı sınırlamak için).
- **Evaluation senaryo dosyası iki farklı yoldan aranır:** geliştirme ortamında (`dotnet run`,
  `ContentRootPath` = proje kökü) ve konteynerde/yayınlanmış binary'de (`ContentRootPath` farklı
  olabilir) aynı kodun çalışabilmesi için.

## 6. Metotlar / Üyeler

### `AgentsEndpoints` (`/agents`, `/escalations/{id}/reroute`)

| Route | Açıklama |
|---|---|
| `GET /agents` | Tüm temsilcileri (registry + auth-linked kullanıcılar birleşik) listeler. |
| `POST /agents` | Yeni temsilci kaydı oluşturur (`HumanAgentInput`). |
| `GET /agents/{id}` | Tek temsilci. |
| `PUT /agents/{id}` | Temsilciyi günceller. |
| `DELETE /agents/{id}` | Temsilciyi siler. |
| `POST /escalations/{id}/reroute` | Bir eskalasyonu manuel olarak başka bir temsilciye yönlendirir. |

### `AnalyticsEndpoints`

| Route | Açıklama |
|---|---|
| `GET /analytics/dashboard` *(Admin)* | Genel istatistik özeti. |
| `GET /analytics/session/{sid}` *(Admin)* | Tek oturum için detaylı analiz. |
| `POST /sessions/{sid}/rating` | Konuşma değerlendirmesi gönderir (`RatingInput { Stars, Feedback }`, `Stars` 1-5 aralığında doğrulanır). |
| `GET /sessions/{sid}/rating` | Bir oturumun mevcut rating'ini döner. |
| `GET /analytics/ratings/recent` *(Admin)* | Son N rating. |

### `EvaluationEndpoints`

| Route | Açıklama |
|---|---|
| `GET /eval/scenarios` | YAML'daki senaryoları özet halinde listeler. |
| `POST /eval/run?limit=` | Tüm (veya `limit` kadar) senaryoyu koşturur, `IEvaluationPort.RunAsync`. |
| `POST /eval/run/{id}` | Tek senaryoyu id ile koşturur. |
| `ResolveScenarioPath(IWebHostEnvironment)` *(private)* | YAML dosyasının gerçek yolunu iki aday arasından bulur. |

### `SlaEndpoints`

| Route | Açıklama |
|---|---|
| `GET /sla/events?count=` | Son N SLA warn/breach olayı. |
| `GET /sla/status` | Güncel bekleyen/açık kuyruk durumu, maksimum yaş, ihlal sayısı. |

### `TelemetryEndpoints` (`/telemetry`)

| Route | Açıklama |
|---|---|
| `GET /telemetry/cost` | Model bazlı token + USD maliyet özeti (`ITelemetryPort.GetCostSnapshot`). |
| `GET /telemetry/cost/models` | Bilinen model listesi. |
| `POST /telemetry/cost/reset` | Maliyet sayaçlarını sıfırlar. |

### `TraceEndpoints`

| Route | Açıklama |
|---|---|
| `GET /traces/recent?count=20` | Son N reasoning trace. |
| `GET /traces/{traceId}` | Tek trace'in tam detayı. |
| `GET /traces/by-session/{sessionId}` | Bir oturuma ait tüm trace'ler. |
| `GET /traces/sessions` | Session bazlı trace özeti (dashboard sidebar için). |
| `GET /traces/stats` | Toplu istatistikler. |

## 7. Bağımlılıklar

Constructor injection yok — her endpoint lambda'sı ilgili `I*Port`'u minimal API parametre
injection'ı ile alır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [Intelligence uç noktaları](Intelligence.md)
