# Agentic Müşteri Destek Botu

**Microsoft Agents Framework (MAF)** ve **.NET 10** üzerine inşa edilmiş, agentik mimarili bir müşteri destek chatbot'u.

Sistem, uzmanlaşmış LLM ajanlarından oluşan bir takımı orkestrasyon mantığıyla çalıştırır. Temel yetenekler:

- **Ürün sorgusu**, **sipariş takibi**, **sipariş oluşturma** ve **şikayet kaydı** işlemleri
- **Structured reasoning** — Her adımda açık akıl yürütme ve kendi kendini doğrulama
- **Human-in-the-Loop (HITL)** — Kritik işlemlerde insan onayı
- **Gerçek zamanlı streaming** — SSE üzerinden canlı yanıt akışı
- **Semantic Memory + RAG** — Qdrant tabanlı bilgi tabanı + episodik bellek (geçmiş konuşmalar)
- **Self-Improving Loop** — Düşük puanlı trace'lerden LLM ile öğrenilmiş "lesson" üretip admin onayıyla bilgi tabanına geri besleme
- **Trace Replay UI** — Bir workflow koşusunu adım adım yeniden oynatma (debug + demo)
- **OpenTelemetry + Maliyet Telemetrisi** — Tüm LLM çağrıları, ajan adımları ve tool kullanımları için OTLP-uyumlu trace + metric (Jaeger/Prometheus/Grafana). Model bazlı USD maliyet ve token muhasebesi.
- **Per-Customer Personalization Memory** — Her müşteri için kalıcı profil (sık niyet, ürün ilgi alanları, dil/ton tercihi, son rating'ler). Heuristik güncelleme + admin tetikli LLM consolidate. Profil ContextPipeline üzerinden tüm ajanlara enjekte edilir.
- **Smart Routing & Skills-Based Escalation** — Eskalasyon oluştuğunda intent + müşteri profili üzerinden gerekli skill tag'leri çıkarılır ve `IHumanAgentRegistry`'deki temsilciler arasında en iyi skill + dil + load match'iyle aday önerilir. Manuel re-route + load tracking + auto-decrement.
- **Parallel SubTask Execution** — Compound query'lerde (ör. "1030 ve 1042 durumu") yan-etkisiz alt görevler (Product/OrderInquiry) `Task.WhenAll` ile paralel çalışır; yan-etkili olanlar (OrderPlacement/Complaint) HITL gate'i nedeniyle sıralı kalır. p50 latency düşer.
- **SLA / Response Time Guardian** — Bekleyen onay ve açık eskalasyonları periyodik tarayan `BackgroundService`. Eşik aşılan onaylar `AutoReject`, eskalasyonların önceliği otomatik **bir kademe yükseltilir** (Low→Normal→High→Critical). Admin `/sla/status` ve `/sla/events` endpoint'lerinden görür.
- **Sesli Konuşma (Realtime)** — OpenAI Realtime API (`gpt-realtime-2.1`) üzerinden tek sesli mod: model **kendisi** konuşur ve function calling yapar; yazılı sohbetin iş tool'larının **tamamı** açıktır (ürün, sipariş ve şikayet sorguları; sipariş oluşturma, iptal, iade, şikayet kaydı; temsilciye yönlendirme). Yan etkili işlemler yazılı sohbetle **aynı HITL onay kapısından** geçer: model ayrıntıları müşteriye teyit ettirir, talep onaya gönderilir, sonuç bildirim olarak gelir. Art arda olumsuz turlarda bir temsilciye eskalasyon açılır. (Eskiden ayrıca yanıtı ajan hattına ürettiren bir "köprü" modu vardı; kaldırıldı.)
  Detay → [`docs/CustomerSupportBot.Adapters.AI/Realtime/`](docs/CustomerSupportBot.Adapters.AI/Realtime/README.md).

---

## İçindekiler

- [Demo](#demo)
- [Genel Bakış](#genel-bakış)
- [Mimari](#mimari)
- [Ajan Takımı](#ajan-takımı)
- [Teknoloji Yığını](#teknoloji-yığını)
- [Başlangıç](#başlangıç)
- [API Endpoint'leri](#api-endpointleri)
- [Human-in-the-Loop (HITL)](#human-in-the-loop-hitl)
- [Değerlendirme ve Test](#değerlendirme-ve-test)
- [Proje Yapısı](#proje-yapısı)
- [Dokümantasyon](#dokümantasyon)
- [Lisans](#lisans)

---

## 🎬 Demo

https://github.com/user-attachments/assets/9cf6ba41-7fa1-49da-ad9b-251ea4b3ae65

> **Not:** Video GitHub'da görüntülenemiyorsa, dosyayı `docs/CustomerSupportBot.Web/1767601282559.mp4` konumunda bulabilirsiniz.

---

## Genel Bakış

Bu proje, müşteri desteği için **çok ajanlı orkestrasyon** desenini gösterir. Tek bir monolitik LLM çağrısı yerine, sistem yapılandırılmış bir iş akışı içinde işbirliği yapan 6 uzman ajan kullanır:

1. **PlanningAgent** — Kullanıcı sorgusunu doğru uzmana yönlendirir.
2. **Uzman Ajanlar** (Product, Order, Complaint) — Alan görevlerini tool çağrıları ile gerçekleştirir.
3. **ResponseAgent** — Self-critique (öz-eleştiri) ile nihai yanıtı sentezler.

Temel yetenekler:

- **Yapılandırılmış reasoning** — Araç çağrısı öncesi kontrol (`preToolCheck`) ve sonrası yansıtma (`postToolReflection`) (ReAct + Self-Reflection desenleri)
- **SSE streaming** — Gerçek zamanlı ajan adım görünürlüğü
- **Human-in-the-Loop (HITL)** — Yan etkili tool'lar (sipariş oluşturma, şikayet kaydı) için açık admin onayı
- **Reasoning trace store** — Hata ayıklama ve denetim için
- **Entity verification** — Bellek içi veritabanına karşı doğrulama
- **Deterministic sanity checking** — Reasoning tutarlılığı için 8 kurallı deterministik doğrulama
- **Compound query orkestrasyonu** (Faz 4b) — Çoklu niyetli kullanıcı mesajları için
- **Evaluation runner** — YAML tabanlı senaryo testleri
- **Canlı admin devralma** — Herhangi bir oturumu bot modundan anlık olarak insan moduna geçirme

---

## Mimari

```
┌──────────────────────────────────────────────────────────────────┐
│           FRONTEND — CustomerSupportBot.Web (Blazor WASM)          │
│         Chat.razor — SSE streaming UI · ayrı host (:5288)          │
└─────────────────────────────┬────────────────────────────────────┘
                              │ HTTP / SSE
┌─────────────────────────────▼────────────────────────────────────┐
│                     ENDPOINTS (Minimal API)                      │
│   ChatEndpoints │ SessionEndpoints │ TraceEndpoints │ Admin    │
└───────┬───────────────────┬──────────────────┬───────────┬─────────┘
        │                   │                  │           │
        ▼                   ▼                  ▼           ▼
┌───────────────┐  ┌────────────────┐  ┌─────────────┐  ┌────────┐
│ ReasoningSvc  │  │ CustomerSupp.  │  │ TraceStore  │  │ Admin  │
│  (reasoning)  │─▶│ Team           │  │ (Postgres/  │  │ (HITL) │
└───┬───────────┘  │ (6 agents +    │  │  InMemory)  │  └────────┘
    │              │  ChatManager   │
    │              │  + Phase 4b     │
    │              │  orchestration) │
    │              └────────┬───────┘
    │                       │
    │         ┌─────────────┴─────────────┐
    │         ▼                           ▼
    │  ┌───────────────┐          ┌───────────────┐
    │  │ PromptService │          │ ContextPipe.  │
    │  │ (MD loader)   │          │ (providers)   │
    │  └───────────────┘          └───────┬───────┘
    │                                     │
    │              ┌──────────────────────┴───────────┐
    │              ▼                                  ▼
    │     SemanticMemory (RAG) ·           ConversationSummary
    │     CustomerProfile · Recommendation (LLM summary)
    │
    │  ┌─────────────────────────────────────────────────────────┐
    └─▶│         Deterministic Reasoning Helpers                 │
       │   EntityVerifier        (güvenli ID resolution)         │
       │   ReasoningSanityChecker(8-rule post-validation)        │
       └─────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ALTYAPI                                    │
│  OpenAI Chat Client (gpt-5.4) │ ReasoningChatClient (gpt-5.4-nano)│
│  PostgreSQL + Redis + Qdrant adapters                            │
└─────────────────────────────────────────────────────────────────┘
```

---

## Ajan Takımı

| Ajan | Rol | Tool'lar | Yapılandırılmış Çıktı |
|------|-----|----------|-----------------------|
| **PlanningAgent** | Niyet tespiti ve yönlendirme | — | `PlanningResult` |
| **ProductAgent** | Ürün sorgusu + katalog listeleme (salt-okunur) | `product_inquiry_tool`, `product_list_tool` | `SpecialistReasoning` |
| **OrderAgent** | Sipariş oluşturma, sorgulama, iptal ve iade (HITL) | `order_placement_tool`, `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`, `order_cancel_tool`, `return_request_tool` | `SpecialistReasoning` |
| **ComplaintAgent** | Şikayet kaydı ve sorgulama | `complaint_registration_tool`, `complaint_status_tool`, `get_all_complaints_tool` | `SpecialistReasoning` |
| **HumanHandoffAgent** | İnsan temsilciye aktarım | `human_handoff_tool` | `SpecialistReasoning` |
| **ResponseAgent** | Nihai yanıt + self-critique | — | `ResponseCritique` |

Her uzman ajan, aşağıdaki **4 adımlı alt-bileşen zincirini** izler:

1. **Input Prep** — Sistem prompt'u + bağlam + reasoning ipucu + entity extraction ipucu
2. **LLM Reasoning** — `preToolCheck` JSON bloğu üretir
3. **Tool Execution** — `canProceed=true` ise MAF tool'u çağırır
4. **Reflection + Kullanıcı Mesajı** — `postToolReflection` + kullanıcıya yönelik Türkçe mesaj

---

## Teknoloji Yığını

| Katman | Teknoloji |
|--------|-----------|
| Framework | .NET 10 (ASP.NET Core Minimal API) |
| Ajan Framework | Microsoft Agents Framework (MAF) 1.22.0 |
| AI Soyutlamaları | `Microsoft.Extensions.AI` |
| LLM Sağlayıcı | OpenAI (`gpt-5.4`, `gpt-5.4-nano`) / Azure OpenAI |
| Embedding | OpenAI `text-embedding-3-large` (3072-dim) |
| Vector Store | **Qdrant** (gRPC, Cosine distance) |
| Kalıcı Veri | PostgreSQL 16 (EF Core 10) + Redis (opsiyonel) |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` 10.0.12 |
| Serileştirme | System.Text.Json (camelCase enum string'leri) |
| YAML Ayrıştırma | YamlDotNet 18.1.0 |
| Arayüz | Blazor WebAssembly (`CustomerSupportBot.Web`) — SSE streaming chat + admin/agent paneli |

---

## Başlangıç

### Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Docker** — PostgreSQL, Redis ve Qdrant container'ları için (önce `cp deploy/.env.example deploy/.env` ile Postgres parolasını verin, sonra `docker compose -f deploy/docker-compose.yml up -d`; ayrıntı: [docs/deployment.md](docs/deployment.md)); entegrasyon testleri de Testcontainers ile Docker ister
- **OpenAI API key** (chat + embedding) veya Azure OpenAI

### Yapılandırma

Gizli değerleri takip edilen `appsettings.json`'a değil, git'e girmeyen `src/CustomerSupportBot.Api/appsettings.Development.json`'a (ya da ortam değişkenlerine) yazın:

```json
{
  "AI": {
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "sk-...",
      "Model": "gpt-5.4",
      "ReasoningModel": "gpt-5.4-nano",
      "ReasoningEffort": "medium"
    }
  },
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5433;Database=CustomerSupportDb;Username=...;Password=...",
    "Redis": "localhost:6380,abortConnect=false"
  },
  "Auth": {
    "DefaultAdminUsername": "admin",
    "DefaultAdminPassword": "..."
  }
}
```

Development ortamında migration'lar otomatik uygulanır ve demo veri (admin, temsilciler, katalog, örnek müşteri hesabı) seed edilir.

#### Sesli görüşme modelleri

Sesli görüşmenin modelleri `AI:Realtime` altında ayarlanır. Development ortamında
`appsettings.Development.json` `appsettings.json`'ı ezer — değişikliği çalıştığınız ortamın dosyasında yapın.
(`appsettings.json`'a yorum satırı eklenemez: testler dosyayı katı JSON olarak okur.)

**Konuşma modeli** — `AI:Realtime:Model`: şu an `gpt-realtime-2.1`.

**Kullanıcı konuşmasının transkripsiyon modeli** — `AI:Realtime:TranscriptionModel`. Kod üç modeli
destekler; OpenAI'ye gönderilecek alanları adaptör model adından kendisi seçer
(bkz. [RealtimeTranscriptionConfig](docs/CustomerSupportBot.Adapters.AI/Realtime/RealtimeTranscriptionConfig.md)):

| Değer | Ne zaman | Ek ayarlar | Fiyat (dk) |
|---|---|---|---|
| `gpt-4o-transcribe` *(varsayılan)* | Önceki nesil, kanıtlanmış | — | — |
| `gpt-transcribe` | **Önerilen aday:** tamamlanmış turu yüksek doğrulukla yazar; sayılar ve kısa cümlelerde daha iyi olduğu belirtiliyor | `TranscriptionKeywords` | 0,0045 $ |
| `gpt-live-transcribe` | Konuşurken anlık yazar: kullanıcı balonunda **canlı altyazı** (diğer modellerde metin konuşma bittikten sonra görünür) | `TranscriptionKeywords`, `TranscriptionDelay` (`minimal`…`xhigh`) | 0,017 $ |

- Yeni modellerin (`gpt-transcribe`, `gpt-live-transcribe`) Türkçe kalitesi canlı bir sesli görüşmede
  doğrulanmadı; geçmeden önce kısa bir deneme yapın.
- `TranscriptionKeywords`'e ve `TranscriptionPrompt`'a **örnek numara/cümle yazmayın** — model
  sessizlikte bu sözlükten metin uydurabilir.
- Modele uymayan bir ayar (ör. `gpt-4o-transcribe` ile `TranscriptionDelay`) gönderilmez, uyarı loglanır.
- Diğer modeller de çalışır ama önerilmez: `gpt-4o-mini-transcribe` (daha az isabetli), `whisper-1`
  (en eski nesil), `gpt-realtime-whisper` (`gpt-live-transcribe`'ın eski sürümü),
  `gpt-4o-transcribe-diarize` (konuşmacı ayırma; tek kişili görüşmede gereksiz).

### Çalıştırma

```bash
dotnet run --project src/CustomerSupportBot.Api   # API — http://localhost:5021
dotnet run --project src/CustomerSupportBot.Web   # Blazor arayüz — http://localhost:5288
```

Chat arayüzü ve admin/agent paneli Blazor uygulamasından (`:5288`) sunulur; API ayrı bir host'tur.

### Hızlı API Testi

Chat uçları `Customer` rolünde bir JWT ister. Önce seed edilen örnek müşteriyle giriş yapın (`Auth:DefaultCustomerEmail` / `Auth:DefaultCustomerPassword`), dönen `accessToken`'ı kullanın:

```bash
TOKEN=$(curl -s -X POST http://localhost:5021/auth/customer/login \
  -H "Content-Type: application/json" \
  -d '{"email": "<musteri-eposta>", "password": "<parola>"}' | jq -r .accessToken)

curl -X POST http://localhost:5021/chat/ \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query": "sipariş 1030 nerede?", "sessionId": null}'
```

---

## API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/chat/` | `POST` | Non-streaming chat |
| `/chat/stream` | `POST` | SSE streaming chat |
| `/sessions/` | `GET` | Oturum listesi (müşteri yalnızca kendi oturumlarını görür) |
| `/sessions/{sessionId}/state` | `GET` | Oturum durumu |
| `/sessions/{sessionId}/messages` | `GET` | Oturum mesajları |
| `/traces/{traceId}` | `GET` | Bir çalıştırma için reasoning trace'i |
| `/traces/by-session/{sessionId}` | `GET` | Oturumun trace'leri |
| `/traces/recent` | `GET` | Son trace'ler |
| `/eval/scenarios` | `GET` | YAML senaryolarını listele (admin) |
| `/eval/run` | `POST` | Tüm YAML senaryo testlerini çalıştır (admin) |
| `/eval/run/{id}` | `POST` | Tek senaryoyu çalıştır (admin) |
| `/approvals/pending` | `GET` | Bekleyen HITL onaylarını listele |
| `/approvals/{id}/approve` | `POST` | Bir tool çağrısını onayla |
| `/approvals/{id}/reject` | `POST` | Bir tool çağrısını reddet |
| `/escalations` | `GET` | Yükseltmeleri listele |
| `/escalations/{id}/resolve` | `POST` | Bir yükseltmeyi çöz |
| `/memory/stats` | `GET` | Qdrant collection sayıları + embedding config (admin) |
| `/memory/search?kind=knowledge&q=...` | `GET` | Semantic memory'de arama (admin) |
| `/memory/ingest` | `POST` | KnowledgeBase/*.md dosyalarını + yayındaki makaleleri yeniden ingest et (admin) |
| `/memory/articles` | `GET` | Bilgi tabanı makalelerini listele (admin) |
| `/memory/articles/{id}` | `GET` | Tek makale (admin) |
| `/memory/articles` | `POST` | Makale oluştur; yayındaysa anında indekslenir (admin) |
| `/memory/articles/{id}` | `PUT` | Makale güncelle; indeks tazelenir, artık chunk'lar silinir (admin) |
| `/memory/articles/{id}` | `DELETE` | Makaleyi ve indeksteki tüm parçalarını sil (admin) |
| `/improvements/mine` | `POST` | Düşük puanlı/hatalı trace'leri tarayıp lesson aday üret (admin) |
| `/improvements?status=Proposed` | `GET` | Lesson'ları listele (Proposed / Approved / Rejected) |
| `/improvements/{id}/approve` | `POST` | Lesson'ı onayla → Qdrant Lessons collection'a yaz |
| `/improvements/{id}/reject` | `POST` | Lesson'ı reddet |
| `/telemetry/cost` | `GET` | Model bazlı toplam token + USD maliyet özetini döndürür (admin) |
| `/telemetry/cost/models` | `GET` | Fiyat tablosunda tanımlı bilinen modelleri listeler (admin) |
| `/telemetry/cost/reset` | `POST` | In-memory maliyet sayaçlarını sıfırlar (admin) |
| `/customers` | `GET` | Profil kaydı olan müşterileri listeler (admin) |
| `/customers/{id}/profile` | `GET` | Belirli müşterinin kalıcı profilini döner (admin) |
| `/customers/{id}/profile/refresh` | `POST` | LLM ile profil özetini ve ton tercihini yeniler (admin) |
| `/customers/{id}/profile/note` | `PUT` | Profile admin notu ekler/günceller (admin) |
| `/customers/{id}/profile` | `DELETE` | Profil kaydını siler (admin) |
| `/agents` | `GET`/`POST` | İnsan müşteri temsilcisi listesi (registry+DB merge) / yeni temsilci oluştur (admin) |
| `/agents/{id}` | `GET`/`PUT`/`DELETE` | Temsilci detay / kısmi güncelle / sil (admin) |
| `/escalations/{id}/reroute` | `POST` | Eskalasyonu manuel olarak başka temsilciye atar (admin) |
| `/agent/escalations/my` | `GET` | Bana atanmış eskalasyonlar (agent) |
| `/agent/escalations/open` | `GET` | Atanmamış + bana atanmış eskalasyonlar — başkasına atananlar gizlenir (agent) |
| `/agent/escalations/{id}/acknowledge` | `POST` | Eskalasyonu üstlen — `assignedTo` otomatik set edilir (agent) |
| `/agent/escalations/{id}/resolve` | `POST` | Eskalasyonu çöz (agent) |
| `/agent/chat-sessions/{sid}/takeover` | `POST` | Session'ı Human moduna al (agent) |
| `/agent/chat-sessions/{sid}/release` | `POST` | Session'ı Bot moduna bırak (agent) |
| `/agent/chat-sessions/{sid}/messages` | `POST` | Müşteriye mesaj gönder (agent) |
| `/agent/profile` | `GET` | Kendi HumanAgent profilini getir (agent) |
| `/sla/status` | `GET` | SLA Guardian güncel durum: pending/open sayı, en eski yaş, ihlal sayısı (admin) |
| `/sla/events` | `GET` | Son SLA warn/breach olayları (admin) |

Tam API referansı için [`docs/CustomerSupportBot.Api/`](docs/CustomerSupportBot.Api/README.md) klasörüne bakın.

---

## Human-in-the-Loop (HITL)

Bot üç HITL mekanizması sunar:

1. **Onay Kuyruğu (bloklamaz)** — Yan etkili dört tool (sipariş oluşturma, iptal, iade, şikayet kaydı) doğrudan çalışmaz: bir onay kaydı oluşturulur ve tur "onaya gönderildi" yanıtıyla hemen biter. Admin karar verdiğinde işlem yürütülür ve sonuç müşteriye bildirim olarak ulaşır (`/chat-sessions/{sid}/approvals/unseen`). Bekleyen onaylar SLA Guardian tarafından izlenir (`Sla:Approvals`); tool listesi `HumanInTheLoop:ToolsRequiringApproval`'dan gelir.
2. **Canlı Devralma** — Admin veya agent, herhangi bir aktif oturumu gerçek zamanlı olarak **Bot** modundan **İnsan** moduna geçirebilir, son kullanıcıyla doğrudan konuşabilir ve tekrar bot'a devredebilir.
3. **Eskalasyon Yönetimi** — Skill-based routing ile eskalasyonlar uygun temsilciye otomatik önerilir veya admin dropdown'dan manuel atama yapar.

**Erişim noktaları**:
- Admin paneli: `/admin` — eskalasyon atama, onay, live takeover, analytics
- Agent paneli: `/admin` (Agent JWT ile giriş) — yalnızca atanmış ve atanmamış eskalasyonlar görünür; "Atama Yap" butonu gizlenir

---

## Değerlendirme ve Test

Senaryo tabanlı değerlendirme, `EvaluationRunner` tarafından yürütülür. Her senaryo canlı iş akışına karşı çalıştırılır ve her kriter için geçti/kaldı raporu sunulur.

```bash
curl -X POST http://localhost:5021/eval/run -H "Authorization: Bearer $ADMIN_TOKEN"
```

---

## Semantic Memory, Self-Improving Loop ve Replay UI

Bot üç ek "akıllı" katman içerir:

### 🧠 Semantic Memory (Qdrant + RAG)
- `KnowledgeBase/*.md` dosyaları (iade politikası, kargo, SSS) startup'ta chunk'lara bölünür, embedding'lenir ve **Qdrant**'a yazılır.
- **Panelden yönetilen makaleler** (`knowledge.articles` tablosu) ikinci bir Knowledge kaynağıdır: destek ekibi `/knowledge` ekranından ekler/düzenler, kaydedildiği anda indekslenir. Dosyalar salt-okunur bir tohum, makaleler ise çalışma zamanında yazılabilir kaynaktır (dosyalar build çıktısına kopyalandığı için runtime'da düzenlenemez). Yalnızca **yayındaki** makaleler indekslenir; yayından kaldırma veya silme, parçaları indeksten de temizler.
- Her workflow tamamlandığında **episodik bellek** (soru + yanıt + intent) yazılır.
- `SemanticMemoryContextProvider` her sorguda Knowledge + Lessons aramasını context pipeline'a enjekte eder (citation'lı).
- Embedding sağlayıcı: OpenAI / Azure OpenAI (`text-embedding-3-large`, 3072-dim).
- Yapılandırma: `appsettings.json > SemanticMemory` (`Enabled`, `TopK`, `MinScore`, `ChunkSize`).
- Endpoints (admin): `/memory/stats`, `/memory/search`, `/memory/ingest`.

### 🎓 Self-Improving Loop
- `LessonMiner` düşük puanlı (`stars ≤ MinRatingForLesson`), hatalı veya sanity-fail trace'leri toplar; LLM'e "Şu durumda Y yap" şeklinde **uygulanabilir dersler** çıkarır.
- Üretilen `Lesson`'lar `Proposed` statüsünde admin panelinde görünür.
- Admin **Approve** ettiğinde lesson Qdrant `Lessons` collection'a yazılır → sonraki konuşmalarda `SemanticMemoryContextProvider` üzerinden context'e döner. Loop kapanır.
- Yapılandırma: `appsettings.json > SelfImprovement`.
- Endpoints (admin): `/improvements/mine`, `/improvements?status=...`, `/improvements/{id}/approve|reject`.
- Admin UI: `/admin` → **Improvements** sekmesi.

### ▶ Replay UI
- `/replay?traceId=<guid>` — bir trace'i adım adım yeniden oynatmaya yarayan görsel araç (Blazor).
- Timeline: `Init → Reasoning → Planning → AgentVisits + SpecialistReasonings + ToolCalls (chronological) → Final`
- Play / pause / step / hız (0.5×–5×) / deep-link (`?traceId=...`).
- Trace dashboard'tan ve admin panelinden "▶ Replay" linki ile erişilebilir.

Detay için: [`docs/intelligence.md`](docs/intelligence.md).

---

## Proje Yapısı

Proje **hexagonal (ports & adapters) mimarisi** ile 9 projeye ayrılmıştır:

```
agentic-customer-support-bot/
├── src/
│   ├── CustomerSupportBot.Domain/            # Domain modelleri + saf iş kuralları
│   │   ├── Model/                           # Entity POCO'lar, VO'lar, senaryo modelleri
│   │   └── Services/                        # Saf domain servisleri (parser'lar, SessionStateExtractor, ...)
│   │
│   ├── CustomerSupportBot.Application/       # Port tanımları + uygulama servisleri
│   │   ├── Ports/
│   │   │   ├── Inbound/                     # IChatPort, IApprovalPort, IInputGuard, ...
│   │   │   └── Outbound/                    # ISessionManager, IPromptRepository, IMessageBusPort, ...
│   │   ├── Services/                        # Chat, Reasoning, Approval, Tools, Memory, Sla, ...
│   │   └── DependencyInjection/
│   │
│   ├── CustomerSupportBot.Adapters.Agents/   # MAF ajan orkestrasyon adaptörü
│   │   ├── CustomerSupportTeam.cs           # 6 MAF ajanı; tekil/bileşik sorgu yönlendirmesi
│   │   ├── CustomerSupportChatManager.cs    # GroupChatManager (seçim + sonlandırma)
│   │   ├── ApprovalGateService.cs
│   │   └── Routing/
│   │
│   ├── CustomerSupportBot.Adapters.AI/       # OpenAI/Azure chat, embedding, Qdrant, Realtime ses
│   │
│   ├── CustomerSupportBot.Adapters.Persistence/ # Kalıcı veri adaptörleri
│   │   ├── EfCore/                          # CustomerSupportDbContext + migrations
│   │   ├── Postgres/                        # PostgresSessionManager, Approvals, vb.
│   │   ├── InMemory/                        # InMemorySessionManager, demo katalog adaptörleri
│   │   ├── FileSystem/                      # FileSystemPromptRepository + PromptOptions
│   │   └── DependencyInjection/
│   │
│   ├── CustomerSupportBot.Adapters.Redis/    # Redis adaptörleri (locking, pub/sub)
│   │   ├── Locking/                         # RedisDistributedLockAdapter
│   │   ├── Messaging/                       # RedisMessageBusAdapter (IMessageBusPort)
│   │   └── DependencyInjection/
│   │
│   ├── CustomerSupportBot.Adapters.Telemetry/ # OpenTelemetry + maliyet telemetrisi
│   │   ├── Chat/
│   │   ├── OpenTelemetry/
│   │   └── DependencyInjection/
│   │
│   ├── CustomerSupportBot.Api/                # Composition root (ASP.NET Core Minimal API)
│   │   ├── Program.cs                       # DI + endpoint mapping
│   │   ├── appsettings.json                 # AI, WorkflowGuards, HITL, Persistence config
│   │   ├── Endpoints/                       # ChatEndpoints, AdminEndpoints, TraceEndpoints, ...
│   │   ├── Prompts/                         # Ajan + servis prompt MD dosyaları
│   │   │   ├── agents/                      # planning-agent.md, product-agent.md, order-agent.md, ...
│   │   │   └── services/                    # reasoning-system.md, routing-rewrite-*.md, ...
│   │   └── KnowledgeBase/                   # RAG dökümanı: iade politikası, kargo, SSS
│   │
│   └── CustomerSupportBot.Web/                # Blazor WASM admin paneli + chat arayüzü
│
├── tests/                                      # Katman başına test projesi (xUnit v3, MTP)
│   ├── CustomerSupportBot.Domain.Tests/
│   ├── CustomerSupportBot.Application.Tests/
│   ├── CustomerSupportBot.Adapters.*.Tests/    # Agents, AI, Persistence, Redis, Telemetry
│   ├── CustomerSupportBot.Api.IntegrationTests/ # WebApplicationFactory
│   ├── CustomerSupportBot.Web.Tests/           # Blazor istemci servisleri (AuthService refresh)
│   └── CustomerSupportBot.Tests.Shared/        # Testcontainers Postgres fixture, fake'ler
│
├── deploy/
│   ├── docker-compose.yml                     # Postgres, Redis, Qdrant, Elasticsearch, Kibana, OTel, Jaeger
│   ├── .env.example                           # Gizli değer şablonu (deploy/.env olarak kopyalanır, git'e girmez)
│   ├── jaeger-v2-config.yaml
│   └── otel-collector-config.yaml
│
├── docs/                                       # Katman/servis bazlı teknik dokümantasyon
└── CustomerSupport.slnx
```
---

## Dokümantasyon

`docs/` altında Türkçe yazılmış detaylı teknik dokümantasyon bulunur. İki katman:

> 🧭 **Hızlı başlangıç — ajan mimarisi görsel haritası:** [`docs/agent-architecture.html`](docs/agent-architecture.html)
> Hangi ajan hangi tool'u çağırıyor, routing kararı nasıl alınıyor, HITL onayı akışı nerede durduruyor.
> Tek dosya, harici bağımlılık yok — tarayıcıda doğrudan açılır.

### Proje-bazlı (hexagonal mimari haritası)

Her proje için: README + her sınıf/dosya grubu için ayrı doküman.

| Klasör | Kapsam |
|---|---|
| [`docs/CustomerSupportBot.Domain/`](docs/CustomerSupportBot.Domain/README.md) | Domain modeller, services (parser/state machine/extractor), WellKnown |
| [`docs/CustomerSupportBot.Application/`](docs/CustomerSupportBot.Application/README.md) | Port servisleri, agent'lar, reasoning, HITL, routing, workflow executor |
| [`docs/CustomerSupportBot.Adapters.Agents/`](docs/CustomerSupportBot.Adapters.Agents/README.md) | MAF agent ekibi, tool kayıtları, approval gate |
| [`docs/CustomerSupportBot.Adapters.AI/`](docs/CustomerSupportBot.Adapters.AI/README.md) | OpenAI/Azure chat, embedding, Qdrant vector, Realtime voice |
| [`docs/CustomerSupportBot.Adapters.Persistence/`](docs/CustomerSupportBot.Adapters.Persistence/README.md) | InMemory + Postgres adaptörleri, EF Core, hybrid cache pattern, auth |
| [`docs/CustomerSupportBot.Adapters.Redis/`](docs/CustomerSupportBot.Adapters.Redis/README.md) | Distributed lock (RedLock), pub/sub message bus |
| [`docs/CustomerSupportBot.Adapters.Telemetry/`](docs/CustomerSupportBot.Adapters.Telemetry/README.md) | OpenTelemetry pipeline, cost calculator, LLM intercept decorator |
| [`docs/CustomerSupportBot.Api/`](docs/CustomerSupportBot.Api/README.md) | HTTP/SSE/WebSocket endpoint'ler, middleware, workers, JWT |
| [`docs/CustomerSupportBot.Web/`](docs/CustomerSupportBot.Web/README.md) | Blazor WASM admin paneli + chat UI, JS interop, voice client |

### Genel bakış / operasyonel rehberler

| Doküman | İçerik |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Üst seviye mimari, bileşen haritası, DI, istek yaşam döngüsü |
| [`docs/agentic-patterns.md`](docs/agentic-patterns.md) | Tasarım desenleri: ReAct, Self-Reflection, Chain-of-Thought, sub-agent vs sub-component |
| [`docs/reasoning.md`](docs/reasoning.md) | Uygulamadaki reasoning pattern'leri: CoT, sanity check, ReAct, decomposition, grounding, handoff, replan |
| [`docs/debugging-chat.md`](docs/debugging-chat.md) | Chat akışını adım adım debug etme: breakpoint noktaları, senaryo bazlı tanı, traceId takibi, yaygın tuzaklar |
| [`docs/intelligence.md`](docs/intelligence.md) | Semantic memory (Qdrant), Self-Improving Loop, Replay UI, Personalization birlikte |
| [`docs/developer-guide.md`](docs/developer-guide.md) | Ajan, tool ve prompt ekleme için geliştirici rehberi |
| [`docs/class-reference.md`](docs/class-reference.md) | Sınıf/arayüz sözlüğü (C# API referansı) |
| [`docs/operations.md`](docs/operations.md) | Uygulama nasıl çalışır? Kurulum, başlangıç sırası, SSE kanalları, sorun giderme |
| [`docs/deployment.md`](docs/deployment.md) | Docker Compose servis haritası, port yapılandırması, production hazırlık |
| [`docs/security.md`](docs/security.md) | JWT kimlik doğrulama, InputGuard, HITL güvenlik, workflow guard'lar |
| [`docs/evaluation.md`](docs/evaluation.md) | Senaryo tabanlı test sistemi, YAML format, CriteriaEvaluator |

---

## Lisans

MIT License — bkz. [`LICENSE`](LICENSE).
