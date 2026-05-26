# Semantic Memory, Self-Improving Loop ve Replay UI

Bu doküman, bot'un dört "akıllı" katmanını detaylı anlatır:

1. **Semantic Memory + RAG** — Qdrant tabanlı bilgi tabanı + episodik bellek
2. **Self-Improving Loop** — Düşük puanlı trace'lerden ders çıkarıp Qdrant'a geri besleme
3. **Replay UI** — Bir trace'i adım adım yeniden oynatma
4. **Per-Customer Personalization Memory** — Müşteri profili (intent freq, ürün ilgi, dil/ton, rating) + admin LLM consolidate

> Bu dört özellik **birlikte** çalışır: Replay → debug → düşük puan → LessonMiner → Approve → Qdrant Lessons → sonraki konuşmalarda context. Personalization ise her müşterinin profilini takip eder.

---

## 1. Semantic Memory (Qdrant + RAG)

### 1.1 Mimari

```
┌─────────────────────────────────────────────────────────────────┐
│  Kullanıcı Sorgusu                                              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ ContextPipeline      │  (Order: Customer→Summary→SemanticMemory)
              └──────────┬───────────┘
                         │
                         ▼
        ┌────────────────────────────────────┐
        │ SemanticMemoryContextProvider      │
        │   1. last user message  → embed    │
        │   2. Knowledge top-K + Lessons top-K│
        │   3. citation'lı context blok üret │
        └──────────┬─────────────────────────┘
                   │ inject as system message
                   ▼
       ┌──────────────────────┐
       │ Workflow agents       │
       └──────────────────────┘

(Trace tamamlandığında — fire-and-forget):
    CustomerSupportTeam.WriteEpisodicMemorySafe()
        → SemanticMemoryService.WriteEpisodeAsync()
        → Qdrant `cs_episodic` collection
```

### 1.2 Bileşenler

| Bileşen | Görev | Dosya |
|---|---|---|
| `IEmbeddingPort` | metin → float[] | `Application/Ports/Driven/AI/IEmbeddingPort.cs` |
| `OpenAiEmbeddingService` | OpenAI / Azure OpenAI embedding API'si (graceful fallback: ApiKey yoksa null client) | `Adapters.AI/Qdrant/OpenAiEmbeddingService.cs` |
| `IVectorMemoryPort` | upsert/search/count soyutlaması | `Application/Ports/Driven/AI/IVectorMemoryPort.cs` |
| `QdrantVectorMemoryStore` | gRPC (port 6334), Cosine, deterministik GUID point-id | `Adapters.AI/Qdrant/QdrantVectorMemoryStore.cs` |
| `SemanticMemoryService` | facade — `Episodic / Lessons / Knowledge` üç collection'ı yönetir | `Application/Services/Memory/SemanticMemoryService.cs` |
| `KnowledgeBaseIngestionService` | `KnowledgeBase/*.md` → chunk → embed → upsert (hash-tabanlı change detection) | `Application/Services/Memory/KnowledgeBaseIngestionService.cs` |
| `SemanticMemoryContextProvider` | her sorguda KB + Lessons aramasını context'e ekler | `Application/Services/Providers/SemanticMemoryContextProvider.cs` |
| `MemoryEndpoints` | admin REST API | `Api/Endpoints/MemoryEndpoints.cs` |

### 1.3 Üç Collection

| Collection | İçerik | Yazan | Okuyan |
|---|---|---|---|
| `cs_knowledge` | Statik dokümanlar (politika, SSS, kargo) | `KnowledgeBaseIngestor` (startup) | `SemanticMemoryContextProvider` |
| `cs_episodic` | Geçmiş konuşmalar (soru + yanıt + intent) | `CustomerSupportTeam` (trace complete) | `SemanticMemoryContextProvider` (opsiyonel — şu an UI üzerinden search) |
| `cs_lessons` | Admin onaylı dersler | `LessonMiner.ApproveAsync` | `SemanticMemoryContextProvider` |

### 1.4 Yapılandırma

```json
"SemanticMemory": {
  "Enabled": true,
  "VectorStore": { "Host": "localhost", "Port": 7334, "UseHttps": false, "ApiKey": "" },
  "Embedding": { "Model": "text-embedding-3-large", "Dimension": 3072 },
  "Collections": {
    "Episodic":  "cs_episodic",
    "Lessons":   "cs_lessons",
    "Knowledge": "cs_knowledge"
  },
  "Retrieval": { "TopK": 4, "MinScore": 0.35, "MaxContextChars": 1800 },
  "KnowledgeBase": { "AutoIngestOnStartup": true, "ChunkSize": 800, "ChunkOverlap": 100 }
}
```

> **Not:** `Enabled=true` ama OpenAI ApiKey boşsa servis crash etmez — `OpenAiEmbeddingService._client` null kalır, search/upsert no-op ile geçer ve uyarı loglanır.

### 1.5 Knowledge Base Ekleme

`CustomerSupportBot/KnowledgeBase/` altına `.md` dosyası ekleyin. Sonraki başlatmada hash değiştiği için otomatik re-ingest tetiklenir. Manuel:

```bash
curl -X POST http://localhost:<port>/memory/ingest \
  -H "Authorization: Bearer <admin_token>"
```

### 1.6 Endpoints (admin-only)

```
GET  /memory/stats                              # her collection nokta sayısı
GET  /memory/search?q=...&kind=knowledge&topK=5
POST /memory/ingest                              # KB'yi yeniden tara
```

### 1.7 Chunking Stratejisi

`KnowledgeBaseIngestor.ChunkText`:
- Paragraf-aware (`\n\n` boundary)
- `ChunkSize` karakteri aşmadan toplar
- `ChunkOverlap` kadar karakteri sonraki chunk'a taşır
- Her chunk için `Tags = { file, chunk_index }` payload'a yazılır

---

## 2. Self-Improving Loop

### 2.1 Akış

```
┌──────────────────────────────────────────────────────────────────┐
│ POST /improvements/mine     (admin tetikler veya cron)           │
└────────────┬─────────────────────────────────────────────────────┘
             │
             ▼
   ┌────────────────────┐
   │ LessonMiner.MineAsync()
   │   • IReasoningTraceStore.GetRecent(N)
   │   • IRatingStore.GetAll() filter stars ≤ 3
   │   • + termination=error/timeout
   │   • + sanityIssues.Severity=Error
   └─────────┬──────────┘
             │  candidate traces
             ▼
   ┌────────────────────┐
   │ LLM (IChatClient)  │  ← BuildAnalysisPrompt: {trace summaries, ratings, sanity}
   └─────────┬──────────┘
             │  JSON: { lessons: [{title, lesson, observation, suggestedAgent}] }
             ▼
   ┌────────────────────┐
   │ Lesson(Status=Proposed) → ILessonStore (in-memory)
   └─────────┬──────────┘
             │
             ▼  Admin Approve
   ┌────────────────────┐
   │ SemanticMemoryService.UpsertAsync(MemoryKind.Lesson)
   │   → Qdrant `cs_lessons` collection
   └────────────────────┘
             │
             ▼
   Sonraki konuşma → SemanticMemoryContextProvider → Lesson context'e döner
```

### 2.2 Bileşenler

| Bileşen | Dosya |
|---|---|
| `Lesson` (model + `LessonStatus` enum) | `Domain/Model/Improvement/Lesson.cs` |
| `ILessonStore` / `InMemoryLessonStore` | `Application/Ports/Driven/` + `Adapters.Persistence/InMemory/` |
| `LessonMiner` (mine + approve + reject) | `Application/Services/Improvement/LessonMiner.cs` |
| `ImprovementsEndpoints` | `Api/Endpoints/ImprovementsEndpoints.cs` |
| Admin UI | `wwwroot/admin.html` (Improvements tab) + `wwwroot/js/improvements.js` |

### 2.3 Yapılandırma

```json
"SelfImprovement": {
  "Enabled": true,
  "MinRatingForLesson": 3,
  "RecentTracesToScan": 50,
  "MiningIntervalHours": 24,
  "RequireApprovalBeforeActivation": true
}
```

### 2.4 Endpoints (admin-only)

```
POST /improvements/mine                  # tarama tetikle
GET  /improvements                       # tüm lesson'lar (?status=Proposed|Approved|Rejected)
GET  /improvements/proposed              # shortcut
GET  /improvements/{id}
POST /improvements/{id}/approve          # body: { decidedBy?, reason? }
POST /improvements/{id}/reject           # body: { decidedBy?, reason? }
```

### 2.5 LLM Prompt Şeması

`LessonMiner.BuildAnalysisPrompt` LLM'den **strict JSON** ister:

```json
{
  "lessons": [
    {
      "title": "Sipariş ID hatalı formatta verildiyse netleştirme sor",
      "lesson": "Kullanıcı '1030' yerine sadece sayı girdiğinde, doğrudan order_status_tool çağırmadan önce sipariş ID doğrulaması yap.",
      "observation": "12 yerine '12' girilen 3 trace'te ProductAgent yanlışlıkla devreye girdi.",
      "suggestedAgent": "OrderAgent"
    }
  ]
}
```

### 2.6 Persistence

`InMemoryLessonStore` default — uygulama yeniden başlatıldığında `Proposed` lesson'lar kaybolur, ama `Approved` olanlar Qdrant'ta kalır (Knowledge gibi ele alınır). İleride `PostgresLessonStore` eklenebilir.

---

## 3. Replay UI

### 3.1 Amaç

Bir trace'in (`Models/ReasoningTrace`) içeriği zaten zengin: `Reasoning`, `Planning`, `AgentVisits[]`, `SpecialistReasonings[]`, `ToolCalls[]`, `FinalResponse`. JSON dump zor okunuyor. Replay UI bu adımları **kronolojik bir timeline**'da gezilebilir hale getirir.

### 3.2 Sayfa

`/replay.html?traceId=<guid>` — admin auth gerekir (auth.js sayfa yüklenirken token kontrol eder).

### 3.3 Timeline Adımları

`TraceReplay.buildSteps()` şu sırayı üretir:

| # | Kind | Kaynak |
|---|---|---|
| 1 | `init` | trace.userQuery |
| 2 | `reasoning` | trace.reasoning (varsa) |
| 3 | `planning` | trace.planning (varsa) |
| 4..N | `agent` / `reasoning` / `tool` | agentVisits + specialistReasonings + toolCalls — `time` ile sıralanır |
| Son | `final` | trace.finalResponse + terminationReason |

### 3.4 Kontroller

- `⏮ ⏯ ▶ ⏭` — first / play-pause / next / last
- Hız: `0.5× / 1× / 2× / 5×` (200ms — 2000ms arası interval)
- Timeline öğesine tıklayarak doğrudan seçim
- URL'de `?traceId=...` deep-link → kopyalanabilir

### 3.5 Veri Kaynağı

Yeni endpoint **gerekmedi** — mevcut `GET /traces/{traceId}` kullanılır. Replay UI hiç ek server-side iş yapmaz; tüm timeline client-side oluşturulur.

### 3.6 Replay → Lesson Pipeline

Admin bir replay sırasında problem fark ettiğinde:
1. Trace'i replay'de inceler (`/replay.html?traceId=...`)
2. Admin paneli → Improvements → "Yeni Tarama Çalıştır"
3. LessonMiner bu trace'i (düşük puanlıysa veya hatalıysa) yakalar
4. LLM ders önerir → admin approve → Qdrant'a yazılır
5. Sonraki sohbette aynı tip soruda lesson context'e gelir

### 3.7 Dosyalar

```
wwwroot/replay.html        # iskelet
wwwroot/css/replay.css     # timeline + detail pane stili
wwwroot/js/replay.js       # TraceReplay class (load + buildSteps + render + transport)
```

---

## 4. Per-Customer Personalization Memory

Semantic memory **konuşma içeriği** üzerinde çalışırken, personalization memory **müşteri davranış profili** üzerinde çalışır. İkisi farklı amaçlara hizmet eder ve birbirini tamamlar.

### 4.1 Çift Katmanlı Profil Güncelleme

```
Workflow tamamlandı  ───► RecordInteraction (heuristik, LLM-siz)
                            • TotalTurns++
                            • IntentFrequency[finalIntent]++
                            • ProductInterests (IProductCatalogRepository substring match)
                            • PreferredLanguage (TR-chars / regex)
                            • RecentRatings (rating geldiyse)

Admin "Refresh"      ───► ConsolidateAsync (LLM)
                            • IChatClient → JSON {summary, preferredTone}
                            • Summary + ton tercihi güncellenir
                            • Heuristik alanlar dokunulmaz
```

Bu ayrım önemli: heuristik tarafı **her turda** (ücretsiz) çalışır; LLM consolidate **opsiyonel** ve admin tetikli — token maliyeti kontrolü.

### 4.2 Context Pipeline'a Enjeksiyon

`CustomerProfileContextProvider` `Order = 6` ile çalışır (CustomerContext = 0 ve SemanticMemory = 7 arasında). `state.CustomerId` set'liyse `## 👤 Müşteri Profili` blok'u system message olarak ajanlara verilir.

| Alan | Örnek Çıktı |
|---|---|
| Top intent'ler | "En sık niyet: order_inquiry (6 kez), complaint (2 kez)" |
| Ürün ilgi alanı | "İlgilendiği ürünler: Dell XPS 15, iPhone 15 Pro" |
| Ton tercihi | "Ton tercihi: concise" |
| Rating özeti | "Son ortalama puan: 4.0/5 (4 oturum)" |
| Admin notu | "Admin notu: VIP müşteri" |

> Bu blok ajanların stil kararına etki eder; **business kararına değil** (örn: VIP kullanıcıya bile aynı iade politikası uygulanır).

### 4.3 Operasyonel

- **Persistance**: `InMemoryCustomerProfileStore` — restart'ta kaybolur. (Roadmap: `PostgresCustomerProfileStore`)
- **Privacy**: `DELETE /customers/{id}/profile` ile profil tamamen silinebilir (GDPR right-to-be-forgotten).
- **Throttle**: `RecordInteraction` LLM kullanmaz — rate limit yok.

---

## 5. Operasyonel Notlar

### 5.1 Docker

```yaml
# docker-compose.yml
qdrant:
  image: qdrant/qdrant:latest
  ports:
    - "7333:6333"   # HTTP / dashboard (http://localhost:7333/dashboard)
    - "7334:6334"   # gRPC (Qdrant.Client default — appsettings Port=7334)
```

```bash
docker compose up -d postgres qdrant
```

### 5.2 İlk Çalıştırma

1. Container'lar yukarı çık
2. `appsettings.json > AI:OpenAI:ApiKey` doldur (embedding için zorunlu)
3. `dotnet run --project CustomerSupportBot`
4. Startup log'u: `KnowledgeBase ingest tamamlandı: N chunk`
5. Sohbet et → her trace tamamlanışta episodic memory'e yazım
6. `/admin.html` → Improvements → "Yeni Tarama Çalıştır" → öneriler

### 5.3 Sorun Giderme

| Belirti | Sebep | Çözüm |
|---|---|---|
| "Qdrant'a bağlanılamadı; semantic memory devre dışı" | Container ayakta değil | `docker compose ps qdrant` |
| Search hep boş dönüyor | `MinScore` çok yüksek veya KB ingest olmamış | `/memory/stats` ile kontrol |
| Embedding ApiKey eksik uyarısı | OpenAI key yok | appsettings.json doldur |
| LessonMiner "candidate=0" | Düşük puan / hata yok | Bot'a bilerek hata yaptırarak test et |
| Replay sayfası 401 | Token yok | `login.html` üzerinden admin giriş |

---

## 6. Gelecek İşler

- Episodic memory'i `SemanticMemoryContextProvider`'a dahil etmek (şu an sadece KB + Lessons context'e dönüyor)
- `PostgresLessonStore` — Proposed lesson'lar restart'ta da kalsın
- LessonMiningHostedService — `MiningIntervalHours`'a göre cron tarzı otomatik mining
- Replay'de "Edit & re-run from this step" — trace'in bir noktasından prompt değiştirip yeniden çalıştırma
