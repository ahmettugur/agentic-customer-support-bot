# Agentic Müşteri Destek Botu

**Microsoft Agents Framework (MAF)** ve **.NET 10** üzerine inşa edilmiş, agentik mimarili bir müşteri destek chatbot'u.

Sistem, uzmanlaşmış LLM ajanlarından oluşan bir takımı orkestrasyon mantığıyla çalıştırır. Temel yetenekler:

- **Ürün sorgusu**, **sipariş takibi**, **sipariş oluşturma** ve **şikayet kaydı** işlemleri
- **Structured reasoning** — Her adımda açık akıl yürütme ve kendi kendini doğrulama
- **Human-in-the-Loop (HITL)** — Kritik işlemlerde insan onayı
- **Gerçek zamanlı streaming** — SSE üzerinden canlı yanıt akışı

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

> **Not:** Video GitHub'da görüntülenemiyorsa, dosyayı `docs/media/1767601282559.mp4` konumunda bulabilirsiniz.

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
│                        FRONTEND (wwwroot/)                          │
│         index.html + chat-ui.js   — SSE streaming UI              │
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
│  (o-series)   │─▶│ Team           │  │ (in-memory) │  │ (HITL) │
└───┬───────────┘  │ (6 agents +    │  └─────────────┘  └────────┘
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
    │     CustomerContext                  ConversationSummary
    │     (FakeDatabase)                   (LLM summary)
    │
    │  ┌─────────────────────────────────────────────────────────┐
    └─▶│         Deterministic Reasoning Helpers                 │
       │   EntityVerifier        (ID extract + DB verify)        │
       │   ReasoningSanityChecker(8-rule post-validation)        │
       └─────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ALTYAPI                                    │
│  OpenAI Chat Client (gpt-4o)  │  ReasoningChatClient (o4-mini)  │
│  FakeDatabase (Product/Order/Complaint)  │  IdExtractor (regex) │
└─────────────────────────────────────────────────────────────────┘
```

---

## Ajan Takımı

| Ajan | Rol | Tool'lar | Yapılandırılmış Çıktı |
|------|-----|----------|-----------------------|
| **PlanningAgent** | Niyet tespiti ve yönlendirme | — | `PlanningResult` |
| **ProductInquiryAgent** | Ürün sorgusu (salt-okunur) | `product_inquiry_tool` | `SpecialistReasoning` |
| **OrderPlacementAgent** | Sipariş oluşturma (yan etkili) | `order_placement_tool` | `SpecialistReasoning` |
| **OrderInquiryAgent** | Sipariş durumu sorgusu | `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool` | `SpecialistReasoning` |
| **ComplaintAgent** | Şikayet kaydı | `complaint_registration_tool` | `SpecialistReasoning` |
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
| Ajan Framework | Microsoft Agents Framework (MAF) 1.1.0 |
| AI Soyutlamaları | `Microsoft.Extensions.AI` |
| LLM Sağlayıcı | OpenAI (`gpt-5.4`, `o4-mini`) |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` 10.0.5 |
| Serileştirme | System.Text.Json (camelCase enum string'leri) |
| YAML Ayrıştırma | YamlDotNet 16.2.1 |
| Arayüz | Vanilla HTML/JS/CSS (SSE streaming chat UI) |

---

## Başlangıç

### Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **OpenAI API key**

### Yapılandırma

`CustomerSupportBot/appsettings.json` dosyasını düzenleyin:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-5.4",
    "ReasoningModel": "o4-mini",
    "ReasoningEffort": "medium"
  }
}
```

### Çalıştırma

```bash
dotnet run --project CustomerSupportBot/CustomerSupportBot.csproj
```

API `https://localhost:<port>` adresinde başlar ve chat arayüzü `wwwroot/index.html` üzerinden sunulur.

### Hızlı API Testi

```bash
curl -X POST http://localhost:<port>/chat/ \
  -H "Content-Type: application/json" \
  -d '{"query": "ORD-1 siparişim nerede?", "sessionId": null}'
```

---

## API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/chat/` | `POST` | Non-streaming chat |
| `/chat/stream` | `POST` | SSE streaming chat |
| `/sessions/{sessionId}` | `GET` | Oturum durumu ve mesajlar |
| `/traces/{traceId}` | `GET` | Bir çalıştırma için reasoning trace'i |
| `/traces/latest/{sessionId}` | `GET` | Oturumun en son trace'i |
| `/evaluation/run` | `POST` | YAML senaryo testlerini çalıştır |
| `/approvals/pending` | `GET` | Bekleyen HITL onaylarını listele |
| `/approvals/{id}/approve` | `POST` | Bir tool çağrısını onayla |
| `/approvals/{id}/reject` | `POST` | Bir tool çağrısını reddet |
| `/escalations` | `GET` | Yükseltmeleri listele |
| `/escalations/{id}/resolve` | `POST` | Bir yükseltmeyi çöz |

Tam API referansı için [`docs/api.md`](docs/api.md) dosyasına bakın.

---

## Human-in-the-Loop (HITL)

Bot iki HITL modunu destekler:

1. **Onay Kuyruğu** — Yan etkili tool'lar (sipariş oluşturma, şikayet kaydı) çalıştırılmadan önce açık admin onayı gerektirir. Zaman aşımı ve otomatik onay politikaları `appsettings.json` üzerinden yapılandırılabilir.
2. **Canlı Devralma** — Admin, herhangi bir aktif oturumu gerçek zamanlı olarak **Bot** modundan **İnsan** moduna geçirebilir, son kullanıcıyla doğrudan konuşabilir ve tekrar bot'a devredebilir.

Admin paneline `/admin.html` adresinden erişilebilir.

---

## Değerlendirme ve Test

Senaryo tabanlı değerlendirme, `EvaluationRunner` tarafından yürütülür. Her senaryo canlı iş akışına karşı çalıştırılır ve her kriter için geçti/kaldı raporu sunulur.

```bash
curl -X POST http://localhost:<port>/evaluation/run
```

---

## Proje Yapısı

```
CustomerSupportBot/
├── Program.cs                    # DI + endpoint mapping
├── appsettings.json              # OpenAI, WorkflowGuards, HITL config
│
├── Agents/
│   ├── CustomerSupportTeam.cs    # 6 MAF ajanı + workflow builder
│   └── CustomerSupportChatManager.cs  # GroupChatManager (seçim + sonlandırma)
│
├── Endpoints/
│   ├── ChatEndpoints.cs          # POST /chat + /chat/stream (SSE)
│   ├── SessionEndpoints.cs       # Oturum sorguları
│   ├── TraceEndpoints.cs         # Reasoning trace erişimi
│   ├── EvaluationEndpoints.cs    # Test koşturucu
│   ├── AdminEndpoints.cs         # HITL onayları + yükseltmeler
│   └── SseWriter.cs              # SSE event yardımcısı
│
├── Evaluation/
│   ├── EvaluationRunner.cs
│   ├── CriteriaEvaluator.cs
│   └── ScenarioModels.cs
│
├── Models/
│   ├── ChatRequest.cs / ChatResponse.cs
│   ├── PlanningResult.cs
│   ├── ReasoningResult.cs / ReasoningStep.cs / ReasoningIssue.cs
│   ├── SpecialistReasoning.cs
│   ├── ResponseCritique.cs
│   ├── ReasoningTrace.cs
│   ├── VerifiedEntities.cs
│   ├── ToolResult.cs
│   ├── FakeDatabase.cs           # Bellek içi Product / Order / Complaint deposu
│   └── ...
│
├── Prompts/
│   ├── agents/                   # 7 ajan instruction prompt'u (markdown)
│   │   ├── planning-agent.md
│   │   ├── product-inquiry-agent.md
│   │   ├── order-placement-agent.md
│   │   ├── order-inquiry-agent.md
│   │   ├── complaint-agent.md
│   │   ├── human-handoff-agent.md
│   │   └── response-agent.md
│   └── services/                 # Reasoning ipuçları, entity extraction ipuçları, vb.
│
├── Services/
│   ├── ReasoningService.cs       # o-series yapılandırılmış reasoning
│   ├── ReasoningSanityChecker.cs # 8 kural deterministik doğrulama
│   ├── EntityVerifier.cs         # ReAct-lite entity grounding
│   ├── RevisionService.cs        # Yanıt revizyon döngüsü
│   ├── PromptService.cs          # Markdown prompt yükleyici
│   ├── ContextPipeline.cs        # Bağlam toplama
│   ├── InMemorySessionManager.cs # Oturum + konuşma deposu
│   ├── InMemoryApprovalQueue.cs  # HITL onay kuyruğu
│   ├── InMemoryChatBridge.cs     # Canlı devralma mesaj köprüsü
│   └── ...
│
├── Tools/
│   └── (Ajanlar tarafından kullanılan AIFunction tool'ları)
│
└── wwwroot/
    ├── index.html                # Chat arayüzü
    ├── admin.html                # Admin / HITL paneli
    ├── traces.html               # Trace görüntüleyici
    ├── js/                       # chat-ui.js, admin.js, vb.
    └── css/
```

---

## Dokümantasyon

`docs/` dizini, Türkçe olarak yazılmış detaylı teknik dokümantasyon içerir:

| Doküman | İçerik |
|---------|--------|
| [`docs/architecture.md`](docs/architecture.md) | Üst seviye mimari, bileşen haritası, istek yaşam döngüsü |
| [`docs/runtime.md`](docs/runtime.md) | Uygulama nasıl çalışır? Kurulum, başlangıç sırası, SSE kanalları, admin paneli, sorun giderme |
| [`docs/agents.md`](docs/agents.md) | Ajan sorumlulukları, alt-bileşen zinciri, iç anatomi |
| [`docs/api.md`](docs/api.md) | Tam HTTP + SSE event referansı |
| [`docs/workflow.md`](docs/workflow.md) | İş akışı fazları, compound query orkestrasyonu |
| [`docs/patterns.md`](docs/patterns.md) | Tasarım desenleri: ReAct, Self-Reflection, Chain-of-Thought, sub-agent vs sub-component |
| [`docs/reasoning.md`](docs/reasoning.md) | Reasoning servisi, sanity check'ler, entity doğrulama, yapılandırılmış çıktı |
| [`docs/developer-guide.md`](docs/developer-guide.md) | Ajan, tool ve prompt ekleme için geliştirici rehberi |
| [`docs/reference.md`](docs/reference.md) | Sınıf/arayüz kontratları (C# API referansı) |

---