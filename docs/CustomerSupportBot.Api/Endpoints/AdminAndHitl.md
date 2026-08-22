# Admin ve Agent (HITL) Uç Noktaları

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/AdminEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/AgentPanelEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

## 1. Ne İşe Yarar

İnsan-döngüde (Human-in-the-Loop / HITL) çalışmayı sağlayan yönetim uç noktaları: bekleyen tool
onaylarını listeleme/karara bağlama, müşteri eskalasyonlarını yönetme ve bir temsilcinin canlı
sohbeti devralması (Live Takeover). `AdminEndpoints` **Admin** rolü için, `AgentPanelEndpoints`
**Agent** rolü için (aynı işlerin agent'a-özel/kapsamlı görünümü) hazırlanmıştır.

## 2. Hangi Amaçla Kullanılır

- Bir tool (sipariş verme, iade, sipariş iptali, şikayet kaydı gibi yüksek riskli işlemler)
  onay gerektirdiğinde admin/agent panelinden `POST /approvals/{id}/approve|reject` çağrılır.
- Bot düşük güvenle yanıt verdiğinde ya da kullanıcı olumsuz duygu gösterdiğinde oluşan
  eskalasyon kayıtları (`GET /escalations/open`) buradan görülüp `acknowledge`/`resolve`/`dismiss`
  edilir.
- Bir temsilci sohbeti tamamen devralmak istediğinde (`POST /chat-sessions/{sid}/takeover`),
  bottan sonraki tüm mesajlar admin tarafından yazılır; `release` ile bot moduna geri döner.

## 3. Sorumlulukları

- HTTP yüzeyi + rol/kapsam kontrolü + `IApprovalPort`/`IEscalationPort`/`IChatSessionPort`/
  `IHumanAgentPort`/`IAgentTeamPort`'a delegasyon. İş mantığının kendisini (karar kalıcılığı,
  cross-pod senkronizasyon, session state makinesi) barındırmaz — bunlar Application katmanındaki
  port implementasyonlarında yaşar.
- `AgentPanelEndpoints` ek olarak **kapsam daraltma** sorumluluğu taşır: bir Agent yalnızca
  kendisine atanmış ya da atanmamış eskalasyonları görebilir/işleyebilir; Admin sınırsızdır
  (`TryResolveEscalationScope`).
- Yüksek riskli tool'larda (`WellKnown.HighRiskTools`) onay gerekçesini (`Reason`) zorunlu kılar —
  audit trail için.
- SSE üzerinden (`GET .../subscribe`) admin/agent panelinin canlı müşteri mesajlarını dinlemesini
  sağlar ([SseWriter](../Infrastructure/SseAndWebSockets.md) kullanılarak).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [IApprovalPort](../../CustomerSupportBot.Application/Ports/Inbound/IApprovalPort.md),
  [IEscalationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEscalationPort.md),
  [IChatSessionPort](../../CustomerSupportBot.Application/Ports/Inbound/IChatSessionPort.md),
  [IHumanAgentPort](../../CustomerSupportBot.Application/Ports/Inbound/IHumanAgentPort.md) —
  gerçek iş mantığını yürüten Application katmanı port'ları (constructor injection değil,
  minimal API lambda parametre injection'ı ile alınır).
- `IAgentTeamPort.GetWorkflowDiagram()` — `GET /workflow/diagram`, MAF workflow topolojisinin
  Mermaid diyagramını döner (debug/dokümantasyon amaçlı, tur/oturumdan bağımsız sabit).
- `Program.cs` — bu iki grubu sırasıyla `RequireAuthorization("Admin")` ve
  `RequireAuthorization("AdminOrAgent")` politikalarıyla sarar (bkz. [AuthServicesExtensions](../Extensions/AuthServicesExtensions.md)).
  **Not:** `AdminEndpoints.cs` içindeki "Production'da bu endpoint'lerin önüne auth (admin role)
  gelmelidir" yorumu eski bir not — JWT tabanlı `Admin` politikası zaten `Program.cs`'te
  uygulanmış durumda, koruma eksik değil.
- `linked_agent_id` JWT claim'i — `UserInfo.LinkedAgentId`'den token'a yazılır, `AgentPanelEndpoints`
  bunu `GetLinkedAgentId` ile okuyup çağıranın hangi `HumanAgentEntity`'ye karşılık geldiğini bulur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **İki ayrı endpoint grubu (Admin vs Agent) neden birleştirilmedi:** Admin sınırsız görünürlüğe
  sahipken Agent yalnızca kendi eskalasyonlarını/onaylarını görmeli. Tek bir grup + rol bazlı
  `if` dallanması, kapsam kontrolünü her endpoint'te tekrar tekrar unutulabilir hale getirirdi;
  ayrı gruplar route seviyesinde net bir ayrım sağlar.
- **`approvals/stuck` neden `approvals/recent`'ten ayrı:** "son N kayıt" listesi trafik arttıkça
  kayar; yürütmesi askıda kalmış (onaylanmış ama tamamlanmamış) bir kayıt sayfalamayla kaybolabilir
  — tam da elle müdahale gerektiren kayıt görünmez olurdu.
- **Yüksek riskli tool onayında gerekçe zorunlu, diğerlerinde opsiyonel:** admin akışını
  gereksiz yere yavaşlatmamak için seçici zorunluluk.
- **`replan` endpoint'i hem eskalasyon kartından hem doğrudan chat-session panelinden
  çağrılabilir:** aynı iş akışının (planlamayı sıfırlama) iki farklı giriş noktası var, ikisi de
  aynı `IChatSessionPort` metoduna gider — kod tekrarı yok, sadece giriş rotası farklı.

## 6. Metotlar / Üyeler

### `AdminEndpoints` (`RequireAuthorization("Admin")`, kök altında)

| Route | Açıklama |
|---|---|
| `GET /approvals/pending` | Onay bekleyen tüm tool çağrıları. |
| `GET /approvals/recent?count=50` | Son N karar (geçmiş). |
| `GET /approvals/stuck` | Onaylanmış ama yürütmesi askıda kalmış kayıtlar (tarih sınırı yok). |
| `GET /approvals/{id}` | Tek onay kaydı. |
| `POST /approvals/{id}/approve` | Onaylar; `body: { Reason?, DecidedBy? }`. Yüksek riskli tool'larda `Reason` zorunlu. |
| `POST /approvals/{id}/reject` | Reddeder; `body: { Reason?, DecidedBy? }`. |
| `GET /escalations/open` \| `/recent` \| `/{id}` | Eskalasyon listeleme/tekil görüntüleme. |
| `POST /escalations/{id}/acknowledge` | Temsilci işi üstlenir, müşteriye sistem mesajı yayınlanır. |
| `POST /escalations/{id}/resolve` \| `/dismiss` | Eskalasyonu çözer/reddeder. |
| `POST /escalations/{id}/replan` | Eskalasyon kartından: session'a replan bayrağı koyar, eskalasyonu otomatik çözer, Human moddaysa Bot'a döndürür, arka planda son mesajı yeniden planlatır. |
| `POST /chat-sessions/{sid}/replan` | Aynı replan akışı, eskalasyonsuz doğrudan session üzerinden. |
| `GET /chat-sessions/active` | Şu an Human modda olan session'lar. |
| `GET /chat-sessions/{sid}/state` \| `/history` \| `/sentiment` | Session kip/geçmiş/duygu durumu. |
| `POST /chat-sessions/{sid}/takeover` | Session'ı Human moda alır. |
| `POST /chat-sessions/{sid}/release` | Session'ı Bot moduna döndürür. |
| `POST /chat-sessions/{sid}/messages` | Admin'in müşteriye yazdığı mesaj. |
| `GET /chat-sessions/{sid}/subscribe` | SSE — bu session'a gelen müşteri mesajlarını canlı dinler. |
| `GET /admin` | `/admin.html`'e yönlendirme. |
| `GET /workflow/diagram` | MAF workflow'unun Mermaid diyagramı (düz metin). |

### `AgentPanelEndpoints` (`RequireAuthorization("AdminOrAgent")`, `/agent` prefix'i)

Yukarıdakiyle aynı işlerin agent-kapsamlı hâli — route'lar `/agent/...` altındadır. Farkları:

| Route | Açıklama |
|---|---|
| `GET /agent/escalations/my` | Yalnızca `linked_agent_id`'ye atanmış/önerilmiş eskalasyonlar. |
| `GET /agent/escalations/open` \| `/recent` | Admin ise sınırsız, Agent ise yalnızca atanmamış + kendisine atanmış kayıtlar (`TryResolveEscalationScope`). |
| `POST /agent/escalations/{id}/acknowledge` | Üstlenen agent'ın yükünü (`IHumanAgentPort.IncrementLoad`) artırır. |
| `POST /agent/escalations/{id}/resolve` | Agent'ın yükünü azaltır (`DecrementLoad`). |
| `GET /agent/profile` | Çağıran kullanıcının bağlı olduğu `HumanAgentEntity` profilini döner. |
| *(approvals, chat-sessions uçları)* | `AdminEndpoints` ile aynı davranış, `decidedBy`/`humanAgent` alanı JWT'deki `linked_agent_id`'den türetilir. |

| Yardımcı üye | Açıklama |
|---|---|
| `GetLinkedAgentId(HttpContext)` | JWT `linked_agent_id` claim'ini okur. |
| `TryResolveEscalationScope(...)` | Admin ise sınırsız, Agent ise kendi kapsamını döner; claim yoksa `400`. |

## 7. Bağımlılıklar

Constructor injection yok — her endpoint lambda'sı ihtiyaç duyduğu port'u (`IApprovalPort`,
`IEscalationPort`, `IChatSessionPort`, `IHumanAgentPort`, `IAgentTeamPort`) minimal API'nin
parametre injection mekanizmasıyla alır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [Chat & Realtime uç noktaları](ChatAndRealtime.md)
