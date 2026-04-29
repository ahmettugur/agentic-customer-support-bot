# Runtime — Uygulama Nasıl Çalışır?

Bu doküman uygulamayı **kuran**, **çalıştıran**, **gözlemleyen** ve **konfigüre eden** bakış açısıyla yazılmıştır. Kod-içi mimari için → [architecture.md](architecture.md), endpoint sözleşmeleri için → [api.md](api.md), pattern detayları için → [patterns.md](patterns.md).

**Bölümler**:

- [1. Hızlı başlangıç](#1-hızlı-başlangıç)
- [2. Konfigürasyon (appsettings + environment)](#2-konfigürasyon-appsettings--environment)
- [3. Uygulama başlangıç sırası](#3-uygulama-başlangıç-sırası)
- [4. Süreçler ve servisler — kim ne yapar?](#4-süreçler-ve-servisler--kim-ne-yapar)
- [5. Bir kullanıcı mesajının uçtan uca yaşam döngüsü](#5-bir-kullanıcı-mesajının-uçtan-uca-yaşam-döngüsü)
- [6. SSE kanalları](#6-sse-kanalları)
- [7. Admin paneli akışları](#7-admin-paneli-akışları)
- [8. Veri yaşam döngüsü ve bellek davranışı](#8-veri-yaşam-döngüsü-ve-bellek-davranışı)
- [9. Gözlemleme ve sorun giderme](#9-gözlemleme-ve-sorun-giderme)

---

## 1. Hızlı başlangıç

### Gereksinimler

- **.NET 10 SDK** (preview)
- **Bir LLM sağlayıcısı**: OpenAI / Azure OpenAI / Anthropic — birinin API anahtarı yeterli
- Modern bir tarayıcı (frontend ES2022 + EventSource kullanır)

### Çalıştırma

```powershell
cd CustomerSupportBot

# Gizli API key (sağlayıcıya göre birini seçin)
dotnet user-secrets init
dotnet user-secrets set "AI:OpenAI:ApiKey" "sk-..."
# veya
dotnet user-secrets set "AI:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AI:AzureOpenAI:ApiKey" "..."
# veya
dotnet user-secrets set "AI:Anthropic:ApiKey" "sk-ant-..."

dotnet run
# → http://localhost:5021
```

### Açılan URL'ler

| URL | Ne için? |
|---|---|
| `http://localhost:5021/` | Müşteri chat arayüzü |
| `http://localhost:5021/admin.html` | Admin paneli (HITL, eskalasyon, replan, traces, evaluation) |
| `http://localhost:5021/swagger` | (yoksa) Yok — endpoint listesi → [api.md](api.md) |

### İlk akış denemesi

1. Müşteri sayfasında "ORD-1 siparişim nerede?" yaz → `OrderInquiryAgent` çalışır.
2. Yeni sekmede `/admin.html` aç → "Traces" sekmesinden son trace'i gör.
3. "Bir Dell XPS 15 sipariş edebilir miyim?" yaz → `order_placement_tool` onay bekler → admin "Approvals" sekmesinden onayla.

---

## 2. Konfigürasyon (appsettings + environment)

Tüm konfigürasyon `CustomerSupportBot/appsettings.json` üzerinden okunur. Override sırası: `appsettings.json` < `appsettings.Development.json` < user secrets < environment variables.

### `AI` bölümü

```json
{
  "AI": {
    "Provider": "OpenAI",                  // OpenAI | AzureOpenAI | Anthropic
    "OpenAI": {
      "ApiKey": "",
      "Model": "gpt-5.4",                  // standart chat — ajanlar, ChatManager
      "ReasoningModel": "o4-mini",         // ön-analiz reasoning service
      "ReasoningEffort": "medium"          // low | medium | high
    },
    "AzureOpenAI": {
      "Endpoint": "https://<resource>.openai.azure.com/",
      "ApiKey": "",
      "Deployment": "gpt-5.4",
      "ReasoningDeployment": "gpt-5.4",
      "ReasoningEffort": "high"
    },
    "Anthropic": {
      "ApiKey": "",
      "Model": "claude-haiku-4-5",
      "ReasoningModel": "claude-haiku-4-5",
      "MaxTokens": 4096
    }
  }
}
```

`AiClientFactory` `Provider` değerine göre yalnızca **o sağlayıcının** alt bloğundaki alanları zorunlu kılar; diğerleri boş kalabilir. Eksik alanlar → uygulama açılışında `InvalidOperationException`.

### `WorkflowGuards`

```json
"WorkflowGuards": {
  "TimeoutSeconds": 180,            // tek workflow turunun max süresi
  "MaxDuplicateToolCalls": 3,       // aynı tool'u N kere arka arkaya çağırırsa devre kesilir
  "MaxTokensPerRequest": 30000,     // bağlam toplam token üst sınırı
  "MaxIterations": 20               // ChatManager max agent geçişi
}
```

Pattern → [patterns.md#9-guardrails--circuit-breaker](patterns.md).

### `HumanInTheLoop`

```json
"HumanInTheLoop": {
  "Enabled": true,
  "ToolsRequiringApproval": ["order_placement_tool", "complaint_registration_tool"],
  "TimeoutSeconds": 60,             // approval bekleme süresi (timeout sonrası reject)
  "AutoApproveOnTimeout": false,
  "EscalationEnabled": true         // false → escalation tool çağrıları no-op
}
```

`Enabled = false` yaparsanız **tüm HITL mekanizması** bypass edilir (klasik bot davranışı). Detay → [api.md#7-admin-endpoints-hitl](api.md#7-admin-endpoints-hitl).

### Logging

`Microsoft.Extensions.Logging` standart yapılandırma; `appsettings.json > Logging > LogLevel` ile kategori bazlı seviye ayarlanabilir. Kritik kategoriler:

- `CustomerSupportBot.Agents.*` — workflow seçim/yönlendirme
- `CustomerSupportBot.Services.ReasoningService` — reasoning JSON parse hataları
- `CustomerSupportBot.Endpoints.AdminEndpoints` — admin işlemleri
- `ReplanBotRun` — admin replan arka plan turları

---

## 3. Uygulama başlangıç sırası

`Program.cs` aşağıdaki sırayla çalışır:

```
1. Config oku             → AI:Provider seçilir
2. AiClientFactory        → IChatClient + ReasoningChatClient (singleton)
3. PromptService          → Prompts/**/*.md eager load (eksikse fail-fast)
4. Domain servisler       → EntityVerifier, ReasoningSanityChecker, ReasoningService,
                            RevisionService, ContextPipeline + IContextProvider'lar
5. HITL altyapısı         → IApprovalQueue, IEscalationSink, IChatModeRegistry, IChatBridge
6. Persistence            → InMemorySessionManager (ISessionManager + IConversationStore aynı instance),
                            InMemoryReasoningTraceStore (ring buffer, max 500)
7. CustomerSupportTeam    → Agent worker'lar (lazy — ilk istekte construct edilir)
8. Endpoint mapping       → MapChatEndpoints, MapSessionEndpoints, MapTraceEndpoints,
                            MapEvaluationEndpoints, MapAdminEndpoints, MapAnalyticsEndpoints
9. Static files           → wwwroot/ (chat + admin UI)
10. app.Run()             → Kestrel dinler (default :5021)
```

DI haritası ayrıntısı → [architecture.md#dependency-injection-haritası](architecture.md#dependency-injection-haritası).

---

## 4. Süreçler ve servisler — kim ne yapar?

### Frontend (statik dosyalar, `wwwroot/`)

| Dosya | Sorumluluk |
|---|---|
| `index.html` + `js/app.js` | Müşteri chat penceresi; `/chat/stream` SSE + persistent `/chat/events/{sid}` |
| `js/chat-ui.js` | Mesaj/typing/banner/rating widget render |
| `js/chat-api-client.js` | `fetch` + SSE parse |
| `admin.html` + `js/admin.js` | Admin tüm sekmeler: Approvals, Escalations, Active Chats, Analytics, Traces, Evaluation |
| `js/traces.js` | Trace dashboard auto-refresh |

### Backend orchestrator katmanı

| Servis | Sorumluluk |
|---|---|
| `ChatStreamOrchestrator` | `/chat/stream` endpoint'inin tüm akışı: mod kontrolü → reasoning stream → workflow stream → critique → revision → done |
| `ChatEventOrchestrator` | Persistent SSE — `/chat/events/{sid}` üzerinde HITL mod değişimi, eskalasyon yaşam döngüsü, admin/system mesajları, `bot_typing` |
| `ReasoningService` | Pre-analysis: entity verification + reasoning LLM çağrısı + sanity check (8 kural) |
| `CustomerSupportTeam` | Workflow yapısı (PlanningAgent → Specialist → ResponseAgent), MAF `GroupChatManager`, compound query decomposition |
| `RevisionService` | Critic-revise döngüsü — düşük completeness yanıtlarını iyileştirir |

### HITL altyapısı

| Servis | Sorumluluk |
|---|---|
| `IApprovalQueue` (`InMemoryApprovalQueue`) | Tool öncesi onay kuyruğu + timeout (default 60s) |
| `IEscalationSink` (`InMemoryEscalationSink`) | Bot'un çözemediği talepler için ticket kuyruğu |
| `IChatModeRegistry` (`InMemoryChatModeRegistry`) | Session başına `(Bot \| Human)` mod takibi + `ModeChanged` event |
| `IChatBridge` (`InMemoryChatBridge`) | User ↔ Admin mesaj köprüsü (`Channel<T>` pub/sub + ring buffer 200) + bot/typing yayını |
| `ApprovalGateService` | Tool lambda'larını sarar; `Enabled=true` ise onay bekletir, false ise pass-through |

### Storage

| Servis | Sorumluluk |
|---|---|
| `InMemorySessionManager` | Session + LLM-facing conversation history (User/Asistan turları). Restart kayıp! |
| `InMemoryReasoningTraceStore` | Ring buffer 500 — her workflow turu için 1 `ReasoningTrace` |
| `IChatBridge` history | Ring buffer 200 — admin paneline tam transkript (Bot/User/Admin/System) |

---

## 5. Bir kullanıcı mesajının uçtan uca yaşam döngüsü

### A) Bot modunda standart akış (`/chat/stream`)

```
[Tarayıcı]                         [Backend]
  │                                  │
  │── POST /chat/stream ───────────▶ ChatEndpoints
  │                                    │── orchestrator.ExecuteAsync
  │                                    │   ├─ registry.GetMode(sid) == Bot ✔
  │                                    │   ├─ HitlStreamSubscription ON
  │                                    │   │   (onay/eskalasyon event'leri SSE'ye forward)
  │                                    │   │
  │                                    │   ├─ Phase 1: ReasoningService.ReasonStreamingAsync
  │◀── reasoning_start              ───┤   │   ├─ EntityVerifier.Verify (deterministic)
  │◀── reasoning_delta × N          ───┤   │   ├─ ReasoningChatClient stream
  │◀── reasoning_complete           ───┤   │   └─ SanityChecker.Check (8 kural)
  │                                    │   │
  │                                    │   ├─ Phase 2: state.Sentiment update (LLM)
  │                                    │   │
  │                                    │   ├─ Phase 3: team.RunStreamingAsync
  │◀── agent (PlanningAgent, run)   ───┤   │   ├─ ChatManager: PlanningAgent
  │◀── agent (PlanningAgent, done)  ───┤   │   ├─ ChatManager: select Specialist
  │◀── agent (Specialist, run)      ───┤   │   ├─ Specialist: preToolCheck → tool call
  │◀── approval_required            ───┤   │   │   ├─ ApprovalGateService.WaitFor
  │     (admin onay verene kadar       │   │   │   │   ▲
  │      orchestrator askıda kalır)    │   │   │   │   │ admin /approvals/{id}/approve
  │◀── approval_resolved            ───┤   │   │   │   ▼
  │                                    │   │   │   ├─ tool exec (FakeDatabase)
  │                                    │   │   │   └─ postToolReflection
  │◀── agent (ResponseAgent, run)   ───┤   │   ├─ ChatManager: ResponseAgent
  │◀── response_start               ───┤   │   ├─ ResponseAgent.RunStreamingAsync
  │◀── response_delta × N           ───┤   │   ├─ TERMINATE detection
  │◀── response_complete            ───┤   │   ├─ Critique parse → ShouldRevise?
  │                                    │   │   └─ (revize gerekirse RevisionService)
  │                                    │   │
  │                                    │   ├─ Phase 4: AddExchange + RecordBotExchange
  │◀── sentiment_update             ───┤   ├─ Phase 5: sentiment SSE
  │◀── done                         ───┘   └─ orchestrator.Done
```

### B) Eskalasyon yolu

ResponseAgent `TERMINATE: reason=escalation_needed` üretirse:

1. `EscalationSink.Submit()` çağrılır → `EscalationCreated` event.
2. Persistent SSE üzerinden müşteriye `handoff_pending` (sarı banner + input lock).
3. Admin panelinde "Escalations" sekmesinde yeni kart belirir.
4. Admin "Resolve/Dismiss/Devral & Sohbet/🔄 Yeniden Planla" seçenekleri arasından birini seçer.

### C) Live Takeover yolu (admin "Devral")

```
ADMIN ──takeover──▶ /chat-sessions/{sid}/takeover
                       └─ registry.TakeOver(sid, agent)
                          └─ ModeChanged → persistent SSE'ye human_joined
                                               └─ müşteri ekranı yeşil banner + input enabled
ADMIN ── text ─────▶ /chat-sessions/{sid}/messages
                       ├─ bridge.PublishAdminMessage → human_message(from=admin)
                       └─ sessions.AppendAssistantMessage (LLM history'sine yaz!)
USER  ── text ─────▶ /chat/stream
                       └─ orchestrator: mode==Human → workflow atla
                          └─ bridge.PublishUserMessage (admin paneline akar)
ADMIN ──release ───▶ /chat-sessions/{sid}/release
                       └─ registry.Release(sid)
                          └─ ModeChanged → human_left
                              └─ müşteri tekrar bot moduna döner
```

Pattern → [patterns.md#203-live-human-takeover](patterns.md#203-live-human-takeover-real-time-agent-handover).

### D) Admin Replan yolu (one-shot planning override)

```
ADMIN ── replan + note ─▶ /chat-sessions/{sid}/replan
                            ├─ state.ForceReplanNextTurn = true
                            ├─ state.ReplanNote = note (opsiyonel)
                            ├─ open escalations.Resolve
                            ├─ if Mode==Human: registry.Release   →  human_left
                            ├─ bridge.PublishSystemMessage         →  human_message(system)
                            └─ _ = RunReplanBotTurnAsync (fire-and-forget):
                                 ├─ bridge.PublishBotTyping(true)  →  bot_typing(on)
                                 ├─ reasoningService.ReasonAsync
                                 ├─ team.RunAsync
                                 │   └─ BuildWorkflowMessagesAsync
                                 │      → System: "ADMIN OVERRIDE...📌 Admin notu: ..."
                                 │      → flag + note temizlenir
                                 ├─ sessions.AppendAssistantMessage
                                 ├─ bridge.PublishBotMessage        →  human_message(bot)
                                 └─ bridge.PublishBotTyping(false)  →  bot_typing(off)
```

Pattern → [patterns.md#204-admin-replan](patterns.md#204-admin-replan-one-shot-planning-override--auto-bot-turn).

---

## 6. SSE kanalları

Uygulama **iki ayrı SSE kanalı** kullanır — birbirini tamamlar:

| Kanal | Endpoint | Yaşam süresi | Gönderdiği event'ler |
|---|---|---|---|
| **Per-request stream** | `POST /chat/stream` | Bir mesaj turu kadar | `session`, `reasoning_*`, `agent`, `response_*`, `error`, `done`, ayrıca o turda fırlayan `approval_*`, `escalation_created`, `human_joined` |
| **Persistent stream** | `GET /chat/events/{sessionId}` | Müşteri sayfası açık olduğu sürece | `session`, `human_joined/message/left`, `handoff_pending/cleared`, `bot_typing` |

Frontend açılışta `EventSource` ile persistent kanalı açar (`app.js _ensurePersistentEvents`), her kullanıcı mesajı için ayrıca per-request stream başlatır. İki kanal aynı `sessionId` etrafında birleşir.

Tam SSE event sözleşmeleri → [api.md#5-sse-event-şemaları](api.md#5-sse-event-şemaları).

---

## 7. Admin paneli akışları

`http://localhost:5021/admin.html` — sekmeli arayüz:

### Sekmeler

| Sekme | Backend kaynak | Ne yapar? |
|---|---|---|
| **Approvals** | `/approvals/pending`, `/approvals/recent` | Tool onay kararı (approve/reject + opsiyonel sebep) |
| **Escalations** | `/escalations/open`, `/escalations/recent` | Bilet kuyruğu — Resolve / Dismiss / Devral & Sohbet / 🔄 Yeniden Planla |
| **Active Chats** | `/chat-sessions/active`, `/chat-sessions/{sid}/state` | Live Human Takeover yapılan oturumlar; tıklayınca chat panel açılır |
| **Analytics** | `/analytics/dashboard`, `/analytics/ratings/recent` | Toplam oturum, intent dağılımı, ortalama puan, son rating'ler |
| **Traces** | `/traces/recent`, `/traces/{id}` | Reasoning trace dashboard (auto-refresh, sanity issues, agent dizilimi) |
| **Evaluation** | `/eval/scenarios`, `/eval/run` | Senaryo bazlı toplu değerlendirme |

### Chat panel (Active Chats üzerinden açılır)

| Buton | Endpoint | Etki |
|---|---|---|
| **Üstlen** (takeover) | `POST /chat-sessions/{sid}/takeover` | Mode=Human, müşteri ekranında yeşil banner |
| **Mesaj gönder** | `POST /chat-sessions/{sid}/messages` | Müşteri ekranına anlık + LLM history'sine asistan turu olarak yazılır |
| **🔄 Yeniden Planla** | `POST /chat-sessions/{sid}/replan` | Opsiyonel bot-içi not + auto-release + arka plan bot turu (§5D) |
| **Sohbeti bitir** (release) | `POST /chat-sessions/{sid}/release` | Mode=Bot, müşteri ekranında bilgilendirme + (4+ mesaj eşiği sağlanırsa) puanlama widget'ı |

### Eskalasyon kartı butonları

- **Resolve** → status=resolved (audit resolution metni)
- **Dismiss** → status=dismissed (yanlış eskalasyon)
- **Devral & Sohbet** → escalation acknowledge + chat panel açar (Live Takeover)
- **🔄 Yeniden Planla** → §5D, eskalasyon otomatik resolve olur

---

## 8. Veri yaşam döngüsü ve bellek davranışı

| Veri | Yer | Sınır | Restart |
|---|---|---|---|
| Session + LLM history | `InMemorySessionManager` | Sınırsız (RAM) | Kayıp |
| Bridge history (admin transcript) | `InMemoryChatBridge` | Ring buffer 200 msg/session | Kayıp |
| Reasoning traces | `InMemoryReasoningTraceStore` | Ring buffer 500 trace global | Kayıp |
| Approval requests | `InMemoryApprovalQueue` | Ring buffer 200 (recent) + active dict | Kayıp |
| Escalation tickets | `InMemoryEscalationSink` | Sınırsız (RAM) | Kayıp |
| Chat mode | `InMemoryChatModeRegistry` | Sınırsız (RAM) | Kayıp |
| Conversation summary | `SessionState.ConversationSummary` (string) | 1 LLM özeti, 8+ mesajda yenilenir | Kayıp |
| Ratings | `AnalyticsService` (memory) | Sınırsız | Kayıp |
| FakeDatabase (siparişler/ürünler/müşteriler) | Static seed | Sabit (kod) | Yeni instance |

**Production'a hazırlama**: tüm in-memory store'lar Redis / SQL / event store ile değiştirilebilir; interface'ler temizdir (bkz. [reference.md](reference.md)).

---

## 9. Gözlemleme ve sorun giderme

### Trace dashboard

`/admin.html` → "Traces" → her workflow turu için:

- Reasoning JSON (intent, steps, sanity issues)
- Agent dizilimi (PlanningAgent → Specialist → ResponseAgent)
- Tool çağrıları + parametreler + sonuç
- ResponseAgent self-critique (completeness, hallucinationRisk)
- Termination reason (`completed | escalation_needed | not_found | error | …`)
- Revision yapıldı mı?

### Yaygın sorunlar

| Belirti | Sebep | Çözüm |
|---|---|---|
| `InvalidOperationException: AI:OpenAI:ApiKey eksik` | Provider seçili ama anahtar yok | `dotnet user-secrets set "AI:OpenAI:ApiKey" "..."` |
| Frontend SSE 5021'e bağlanmıyor | CORS / port farkı | `app.js` içinde `new ChatApp("http://localhost:5021")` |
| Reasoning timeout | `WorkflowGuards:TimeoutSeconds` çok düşük veya model yavaş | TimeoutSeconds artır veya `ReasoningEffort: "low"` |
| `MaxDuplicateToolCalls` tetiklendi | Bot aynı tool'u 3+ kez çağırıyor | Reasoning prompt iyileştirmesi; `agents.md`'deki `preToolCheck` kuralı |
| Approval expired | Admin 60 saniye içinde karar vermedi | `HumanInTheLoop:TimeoutSeconds` artır veya `AutoApproveOnTimeout` aç |
| Replan'dan sonra bot yine yanlış agent'ı seçti | Reasoning history'de önceki tool sonuçları hâlâ etkili | Replan modal'ından **bot-içi not** ekle ("şikayet ajanına yönlendir" gibi) |
| Admin sohbeti bittiğinde puanlama çıkmıyor | 4 mesajdan az olabilir veya zaten gösterilmiş | `_maybeShowRating` koşulları (`messageCount >= 4`, `!ratingShown`, `!humanModeActive`) |

### Loglama ipuçları

```jsonc
"Logging": {
  "LogLevel": {
    "Default": "Warning",
    "CustomerSupportBot": "Debug",        // tüm app
    "CustomerSupportBot.Agents": "Information",
    "ReplanBotRun": "Debug"               // arka plan replan turları
  }
}
```

---

## Çapraz referanslar

- **Mimari + DI haritası** → [architecture.md](architecture.md)
- **Endpoint sözleşmeleri + SSE event şemaları** → [api.md](api.md)
- **Tasarım pattern'leri (HITL, Replan, Compound query, …)** → [patterns.md](patterns.md)
- **Agent davranış sözleşmeleri** → [agents.md](agents.md)
- **Workflow + ChatManager mantığı** → [workflow.md](workflow.md)
- **Reasoning pipeline ve sanity rule'lar** → [reasoning.md](reasoning.md)
- **Class/interface sözleşmeleri** → [reference.md](reference.md)
- **Yeni feature/agent/tool ekleme** → [developer-guide.md](developer-guide.md)
