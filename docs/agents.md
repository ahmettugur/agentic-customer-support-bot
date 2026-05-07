# Agents

Sistemde **6 ajan** vardır. Hepsi MAF `ChatClientAgent` olarak `@Agents/CustomerSupportTeam.cs`'de oluşturulur ve aynı `IChatClient` (gpt-4o) üzerinde çalışır. Her ajanın **instructions**'u `Prompts/agents/<name>.md` dosyasından okunur.

## Sorumluluk matrisi

| Ajan | Rolü | Tool? | Structured output? | Pozisyon |
|---|---|---|---|---|
| **PlanningAgent** | Router — niyet tespiti + ajan seçimi | Yok | ✅ `PlanningResult` JSON | Turun **başı** |
| **ProductInquiryAgent** | Ürün sorgusu (read-only) | `product_inquiry_tool` | ✅ `SpecialistReasoning` JSON | Specialist |
| **OrderPlacementAgent** | Sipariş oluşturma (yan etkili) | `order_placement_tool` | ✅ `SpecialistReasoning` JSON | Specialist |
| **OrderInquiryAgent** | Sipariş sorgusu (read-only) | `order_status_tool` + `get_last_order_tool` + `get_all_orders_tool` | ✅ `SpecialistReasoning` JSON | Specialist |
| **ComplaintAgent** | Şikayet kaydı (yan etkili) | `complaint_registration_tool` | ✅ `SpecialistReasoning` JSON | Specialist |
| **ResponseAgent** | Son yanıt + TERMINATE | Yok | — | Turun **sonu** |

**Pozisyon** sütunu kritiktir — ChatManager ajan seçimini pozisyon tabanlı yapar: ilk tur PlanningAgent, spesialist tool çağırdıktan sonra ResponseAgent, TERMINATE ile sonlanır. Bkz. [workflow.md](workflow.md).

## Genel çalışma prensibi

Tüm specialist ajanlar aynı desenleri takip eder:

1. **Pre-tool check**: Tool çağırmadan önce `preToolCheck` JSON bloğu üretilir — gereken parametreler, toplananlar, eksikler, `canProceed`.
2. **Tool call** (`canProceed=true` ise): MAF otomatik olarak `AIFunctionFactory.Create()` ile oluşturulmuş function invoke eder; `ToolResult` zarfı döner.
3. **Post-tool reflection**: Tool sonrası `postToolReflection` JSON bloğunda `status` (done/needs_followup/needs_escalation/failed/partial) ve `handoffSuggestion` alanları doldurulur.
4. **Kullanıcıya mesaj**: JSON bloğundan sonra Türkçe, kısa kullanıcı mesajı yazılır (bu mesaj teknik JSON içermez — ChatManager ve `StripTechnicalJsonBlocks` sızıntıları temizler).

Bu yapı **Self-Reflection** + **ReAct** pattern'lerinin birleşimidir, detayı [patterns.md](patterns.md)'de.

---

## Agent iç anatomisi (sub-component zinciri)

Bir ajan MAF'ta **monolitik bir LLM çağrısı** değildir. Tek bir agent iterasyonu **4 sub-component**'ten oluşur. Her sub-component'in girdisi, çıktısı ve başarısızlık davranışı ayrıdır. Aynı 4'lü zincir 6 ajanın hepsinde (PlanningAgent hariç, bakınız not) çalışır — sadece prompt ve tool farklıdır.

### Genel sub-component zinciri

```
┌──────────────────────────────────────────────────────────────────┐
│   [Agent iterasyonu başlıyor]  — ChatManager bu agent'ı seçti    │
└──────────┬───────────────────────────────────────────────────────┘
           ▼
┌──────────────────────────────────────────────────────────────────┐
│ Sub-component 1: INPUT PREP                                       │
│  ─────────────────────────────                                    │
│  • System prompt (Prompts/agents/<name>.md — PromptService'ten)  │
│  • Context Pipeline çıktısı (ilk turda)                          │
│  • Reasoning hint (reasoning-hint.md formatında)                 │
│  • Entity extraction hint (IdExtractor.BuildHintMessage)         │
│  • Konuşma geçmişi (ChatMessage[])                               │
│  • Önceki agent mesajları (intra-workflow)                       │
└──────────┬───────────────────────────────────────────────────────┘
           ▼
┌──────────────────────────────────────────────────────────────────┐
│ Sub-component 2: LLM REASONING (pre-tool check)                   │
│  ─────────────────────────────────────────────                    │
│  • Agent kendi prompt'unda tanımlı JSON'u üretir:                │
│    - Specialist → preToolCheck { requiredParams, missing,        │
│                                  canProceed, confidence, ... }   │
│    - Planning   → { detectedIntent, selectedAgent, ... }         │
│    - Response   → kullanıcıya yönelik nihai yanıt + TERMINATE   │
│  • Parser: SpecialistReasoningParser / PlanningResultParser       │
│    — 3 katmanlı JSON extract (fenced → triple backtick → raw {})  │
│  • Hata → next sub-component atlanır                              │
└──────────┬───────────────────────────────────────────────────────┘
           ▼  (canProceed=true ise)
┌──────────────────────────────────────────────────────────────────┐
│ Sub-component 3: TOOL EXECUTION  (sadece specialist'lerde)       │
│  ──────────────────────────────────                              │
│  • MAF AIFunctionFactory, function call'u execute eder:          │
│    ProductInquiryTool / OrderPlacementTool / ...                 │
│  • Dönüş: ToolResult {Success, Confidence, Message, Data,        │
│                       Error?, SuggestedAction}                    │
│  • FakeDatabase state değişir (side-effect varsa — lock altında) │
│  • Trace'e ToolInvocation kaydı eklenir                          │
└──────────┬───────────────────────────────────────────────────────┘
           ▼
┌──────────────────────────────────────────────────────────────────┐
│ Sub-component 4: REFLECTION + USER MESSAGE                        │
│  ──────────────────────────────────────                          │
│  • Tool çıktısına göre:                                           │
│    - postToolReflection JSON'u doldurulur (status, handoff, ...) │
│    - Kullanıcıya yönelik Türkçe kısa mesaj yazılır                │
│  • ChatManager.SelectNextAgentAsync bu iterasyon sonunda          │
│    handoffSuggestion'ı okuyarak bir sonraki agent'ı seçer         │
│  • StripTechnicalJsonBlocks: JSON blokları kullanıcıya sızmaz     │
└──────────┬───────────────────────────────────────────────────────┘
           ▼
 [Agent iterasyonu bitti → ChatManager sıradaki agent'ı seçer]
```

### Ajan bazlı sub-component varyasyonları

**PlanningAgent** — tool yok, sub-component 3 **atlanır**:

| Sub-component | Davranış |
|---|---|
| 1. Input Prep | Context + reasoning + entity hint + user query |
| 2. LLM Reasoning | `PlanningResult` JSON (intent, selectedAgent, alternativesRejected) + routing satırı |
| 3. Tool | ❌ Atlandı |
| 4. Reflection | ChatManager routing kararı (JSON'daki `selectedAgent`) |

**4 Specialist** (ProductInquiry, OrderPlacement, OrderInquiry, Complaint) — **tam 4-aşamalı zincir**:

| Sub-component | Davranış |
|---|---|
| 1. Input Prep | + tool kataloğu ([Description] attribute'larından) |
| 2. LLM Reasoning | `preToolCheck` JSON + tool invoke decision |
| 3. Tool | 1-3 tool çağrısı arka arkaya (OrderInquiry `get_last_order + order_status` gibi) |
| 4. Reflection | `postToolReflection.status + handoffSuggestion` |

**ResponseAgent** — tool yok, farklı bir "reflection" yapısı:

| Sub-component | Davranış |
|---|---|
| 1. Input Prep | + önceki specialist çıktıları (resultNotes, ToolResult özetleri) |
| 2. LLM Reasoning | **Draft yanıt** + kullanıcıya Türkçe mesaj |
| 3. Tool | ❌ Atlandı |
| 4. Reflection | TERMINATE marker |

### Sub-component'ler arası veri akışı

Bir ajanın sub-component 2 çıktısı (`SpecialistReasoning`, `PlanningResult`) parser'lar tarafından yapılandırılır ve **iki yere** yazılır:

1. **`ReasoningTrace`** içine — `ReasoningTrace.SpecialistReasonings[]`, `Planning` alanlarına
2. **Workflow'un bir sonraki iterasyonuna** — agent mesajı olarak görünür, ChatManager ve sonraki agent'lar okur

`StripTechnicalJsonBlocks` (workflow output temizleme) **sadece kullanıcıya dönen final response**'u temizler — agent'lar arası mesajlarda JSON blokları görünmeye devam eder (onlara gerekli).

### Composition pattern: sub-agent mı, sub-component mı?

Literatürde "sub-agent" tipik olarak **ayrı bir LLM çağrısı yapan, ana agent'ı çağıran yardımcı agent**'ı ifade eder (ör. AutoGPT'de "sub-task agent").

Bu sistemde gerçek anlamda "sub-agent" kavramı **iki yerde** vardır:

1. **Compound query sub-workflow run'ları** — compound query'de her subtask için **recursive `RunAsync`** çağrılır. Her recursive run **tam bir alt-workflow** çalıştırır (6 agent'lı). Bu yüzden tipik "sub-agent" kavramının üst varyantı — "sub-workflow" daha doğru bir terim. Bkz. [workflow.md#compound-query-orkestrasyonu](workflow.md#compound-query-orkestrasyonu).

2. **Context Provider'lar** (`IContextProvider`) — LLM çağrısı **yapabilen** yardımcı bileşenler. `ConversationSummaryProvider` kendi LLM çağrısını yapar (eski konuşma özetler). Ama bunlar "agent" değil; prompt üreten servislerdir.

Bunlar dışında 6 ana ajan **peer**'dir (eşit seviyede) — biri diğerinin sub-agent'ı değil. Tüm orkestrasyon **ChatManager** tarafından yönetilir.

---

## 1. PlanningAgent

**Dosya**: `@Prompts/agents/planning-agent.md`
**Kod**: `@Agents/CustomerSupportTeam.cs:60-65`
**Çıktı modeli**: `@Models/PlanningResult.cs`
**Parser**: `@Services/PlanningResultParser.cs`

### Sorumluluk

Kullanıcının niyetini tespit eder ve **tek bir specialist ajana** yönlendirir. Confidence düşükse veya kritik bilgi eksikse ResponseAgent'a düşürüp kullanıcıdan netleştirme ister.

### Girdi bağlamı

PlanningAgent'a şu system mesajları workflow tarafından enjekte edilir (sırayla):

1. **Context Pipeline çıktısı** — `[Müşteri Bağlamı — CUST-001] ...` müşteri geçmişi + konuşma özeti
2. **Reasoning Hint** — `[ÖN-ANALİZ REASONING ÇIKTISI]` bölümü (`Prompts/services/reasoning-hint.md`); **Deterministic reasoning** ile birlikte:
   - Başta normal reasoning çıktısı (intent, steps, requiredInfo, nextAction)
   - Eğer `subTasks.Count >= 2` ise ek bir **`⚠️ COMPOUND QUERY`** bloğu: her alt görev ayrı satırda (agent + description + entity'ler)
3. **Entity Extraction Hint** — `[ENTITY EXTRACTION]` bölümü (deterministik regex ile çıkarılmış ID'ler + sipariş sorgusu öncelik kuralı)
4. **Konuşma geçmişi** — önceki turlar
5. **Güncel kullanıcı mesajı**

### Çıktı kontratı

İki bölüm:

**Bölüm 1 — JSON (```json …```)**:

```json
{
  "detectedIntent": "sipariş_oluşturma | sipariş_sorgulama | ürün_bilgisi | şikayet | genel",
  "intentConfidence": 0.0-1.0,
  "supportingEvidence": ["alıntılar"],
  "selectedAgent": "OrderInquiryAgent",
  "rationale": "…",
  "alternativesRejected": [{ "agent": "…", "reason": "…" }],
  "needsClarification": false,
  "clarificationQuestion": null,
  "taskDescription": "…"
}
```

**Bölüm 2 — Routing satırı**:

```
1. OrderInquiryAgent : ORD-1 siparişinin durumunu sorgula
```

### Kritik kurallar (Prompts/agents/planning-agent.md'den)

- **Clarification eşiği**: `intentConfidence < 0.7` → `needsClarification=true`, `selectedAgent=ResponseAgent`.
- **Sipariş sorgulama öncelik kuralı**:
  - `order_id` varsa → `OrderInquiryAgent` (customer_id ISTEME)
  - Sadece `customer_id` varsa → `OrderInquiryAgent` (order_id ISTEME; tool son siparişi getirir)
  - İkisi de yoksa → `ResponseAgent` (tek mesajda "sipariş no VEYA müşteri kimliği" iste)
- **Şikayet kuralı**: `order_id` zorunlu; `customer_id` eksikse tool siparişten türetir — kullanıcıya TEKRAR sorma.
- **Çoklu eksik bilgi**: Sipariş OLUŞTURMA gibi gerçekten birden fazla zorunlu alanda eksiklik varsa **tek clarification mesajında hepsini birlikte iste** (ping-pong yasak).
- **alternativesRejected** en az 1-2 alternatifle doldurulmalı — neden seçilmediği açıklanmalı. Bu "negatif gerekçe" izleme için değerlidir.
- **COMPOUND QUERY davranışı**: Reasoning hint'inde `COMPOUND QUERY` etiketi görürse `taskDescription`'ı tüm alt görevleri kapsayacak şekilde yazmalı, Routing bölümünde her alt görev için **ayrı satır** üretmelidir. `selectedAgent` = ilk alt görevin agent'ı (ama gerçek orkestrasyon `CustomerSupportTeam` kod katmanında yapılır — bkz. [workflow.md](workflow.md) ve [reasoning.md](reasoning.md)).

### ChatManager ile etkileşimi

`@Agents/CustomerSupportChatManager.cs:80-106` — PlanningAgent mesajı geldikten sonra:

- `needsClarification=true` VEYA `intentConfidence < 0.7` → `ResponseAgent`'a yönlendirilir (clarification için)
- Aksi halde `selectedAgent` alanındaki ajan çağrılır
- Compound query'de sadece **ilk** alt görevin agent'ı seçilir; ikinci ve sonrakiler `CustomerSupportTeam.RunDecomposedAsync/RunDecomposedStreamingAsync` tarafından ayrı workflow run'ları olarak yürütülür.

---

## 2. ProductInquiryAgent

**Dosya**: `Prompts/agents/product-inquiry-agent.md`
**Kod**: `CustomerSupportTeam.cs:68-73`
**Tool**: `CustomerSupportTools.ProductInquiryTool(productName)` → `ToolResult`

### Sorumluluk

Ürün kataloğundan ürün bilgisi (fiyat, stok) getirir. **Okuma-only**, side-effect yok.

### Girdi

`product_name` (opsiyonel — genel kategori sorularında boş olabilir).

### Çıktı şeması

```json
{
  "preToolCheck": {
    "requiredParams": ["product_name (opsiyonel)"],
    "collectedParams": ["Dell XPS 15"],
    "missingParams": [],
    "canProceed": true,
    "reasoning": "ürün adı elde edildi",
    "confidence": 0.95
  },
  "resultConfidence": 1.0,
  "resultNotes": "Dell XPS 15: 1500$, stok 10",
  "postToolReflection": {
    "taskComplete": true,
    "status": "done",
    "handoffSuggestion": "ResponseAgent",
    "handoffReason": "ürün bilgisi sağlandı",
    "missingContext": [],
    "summary": "Ürün bilgisi başarıyla iletildi"
  }
}
```

### Handoff kuralları

| Sonuç | status | handoffSuggestion |
|---|---|---|
| Ürün bulundu | `done` | `ResponseAgent` |
| `PRODUCT_NOT_FOUND` | `partial` (resultConfidence=0.4) | `ResponseAgent` |
| Kullanıcı ürünü satın almak istiyor | `done` | `OrderPlacementAgent` |

---

## 3. OrderPlacementAgent

**Dosya**: `Prompts/agents/order-placement-agent.md`
**Kod**: `CustomerSupportTeam.cs:76-81`
**Tool**: `CustomerSupportTools.OrderPlacementTool(productName, quantity, customerId)`

### Sorumluluk

Yeni sipariş oluşturur. **Yan etkili** — `FakeDatabase.OrdersDb`'ye yazar, stok düşürür. Yanlış çağrı maliyetli olduğundan pre-tool check **zorunludur**.

### Gerekli parametreler

- `customer_id` (ör. `CUST-001` veya `CUST-1990`)
- `product_id` / `product_name`
- `quantity` (pozitif tamsayı)

### ToolResult davranışları

| Senaryo | Error code | Status | Mesaj |
|---|---|---|---|
| 3 alan da var, ürün + stok tamam | - | `Success=true` | "Sipariş oluşturuldu: ORD-N" |
| Eksik alan | `MISSING_REQUIRED_FIELD` (category=validation) | `Success=false` | Eksik alanları listele |
| Ürün bulunamadı | `PRODUCT_NOT_FOUND` | `Success=false` | "'X' ürünü yok" |
| Stok yetersiz | `STOCK_INSUFFICIENT` | `Success=false` | "Sadece N adet stokta" |

> Error code string'leri `WellKnown.ToolErrorCodes` (`MissingRequiredField`, `ProductNotFound`, `StockInsufficient`) sabitleri olarak yaşar; tool kodu hard-coded değer kullanmaz.

Tool `@Tools/CustomerSupportTools.cs:55-115` — stok kontrolü `lock` altında, idempotent değildir (ancak race condition korumalıdır).

### Handoff kuralları

| Sonuç | status | handoffSuggestion |
|---|---|---|
| Sipariş oluştu | `done` | `ResponseAgent` |
| Eksik param | `needs_followup` | `ResponseAgent` (clarification) |
| `STOCK_INSUFFICIENT` / tool hatası | `failed` | `ResponseAgent` |
| Ödeme/sistem sorunu | `needs_escalation` | `ResponseAgent` (insan desteği) |
| Kullanıcı sonrasında fiyat sordu | - | `ProductInquiryAgent` |

---

## 4. OrderInquiryAgent

**Dosya**: `Prompts/agents/order-inquiry-agent.md`
**Kod**: `CustomerSupportTeam.cs:84-93`
**Tool'lar**: 3 tanesi — `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`

### Sorumluluk

Sipariş durumu/geçmişi sorgular. **Okuma-only**. Tool seçimi LLM'e bırakılır ama entity extraction ile deterministik ipucu verilir.

### Tool seçim öncelik kuralı (kritik)

IdExtractor'ın `@Services/IdExtractor.cs:118-157` ürettiği hint mesajı bu kuralı LLM'e dikte eder:

```
1) order_id VAR → order_status_tool (customer_id İSTEME)
2) order_id YOK, customer_id VAR:
   - "tüm siparişlerim" / "sipariş geçmişim" dendi mi?
     → EVET: get_all_orders_tool
     → HAYIR: get_last_order_tool (varsayılan)
3) İkisi de YOK → tool çağırma, clarification iste
```

Bu kural **hem** prompt'ta (`order-inquiry-agent.md`), **hem** entity extraction hint'inde (`IdExtractor.BuildHintMessage`), **hem** reasoning service prompt'unda (`reasoning-system.md`) tekrar ettirilir — üç katmanlı tutarlılık garantisi.

### ToolResult davranışları

| Tool | Sonuç | Error code |
|---|---|---|
| `order_status_tool` | Bulunamadı | `ORDER_NOT_FOUND` |
| `get_last_order_tool` / `get_all_orders_tool` | Müşterinin siparişi yok | `NO_ORDERS_FOR_CUSTOMER` |

### Handoff kuralları

- Sipariş bulundu → `done` / `ResponseAgent`
- Bulunamadı → `partial` (resultConfidence=0.4) / `ResponseAgent`
- Hiç ID yok → `needs_followup` / `ResponseAgent` (tek mesajda "HERHANGİ BİRİ" iste)
- Kullanıcı sonrasında şikayet → `ComplaintAgent`
- Kullanıcı sonrasında yeni sipariş → `OrderPlacementAgent`

---

## 5. ComplaintAgent

**Dosya**: `Prompts/agents/complaint-agent.md`
**Kod**: `CustomerSupportTeam.cs:96-101`
**Tool**: `complaint_registration_tool(orderId, complaintText, customerId?)`

### Sorumluluk

Şikayet kaydı oluşturur. **Yan etkili** — `FakeDatabase.ComplaintsDb`'ye yazar.

### Önemli: customer_id opsiyoneldir

`@Tools/CustomerSupportTools.cs:157-218` — eğer `customer_id` boş gelirse tool, `order_id`'ye bakarak siparişin sahibini alır ve otomatik doldurur. Eğer hem kullanıcı hem de sipariş sahibi belirtilmişse tutarsızlıkta `CUSTOMER_ID_MISMATCH` conflict döner.

Bu davranış **ping-pong'u önler** — kullanıcı şikayet için `CUST-001` ve `ORD-1` verdiyse iki kez sorulmaz. Prompt'ta da net: `requiredParams: ["order_id", "description"]`, customer_id `optionalParams`'ta.

### ToolResult davranışları

| Sonuç | Error code |
|---|---|
| Kayıt oluştu | - |
| Eksik alan | `MISSING_REQUIRED_FIELD` |
| order_id bulunamadı | `ORDER_NOT_FOUND` |
| customer_id tutarsız | `CUSTOMER_ID_MISMATCH` |

### Handoff kuralları

- Şikayet kaydedildi → `done` / `ResponseAgent`
- Eksik param → `needs_followup` / `ResponseAgent`
- İade/değişim → `needs_escalation`
- Sipariş bulunamazsa → `OrderInquiryAgent`

---

## 6. ResponseAgent

**Dosya**: `Prompts/agents/response-agent.md`
**Kod**: `CustomerSupportTeam.cs:106-110`
**Tool**: Yok.
### Sorumluluk

Diğer ajanlardan gelen bilgiyi **temiz, samimi ve empatik bir Türkçe yanıta** çevirir ve TERMINATE marker'ı ile sonlandırır.

### Çıktı formatı (sırayla)

```
1) Kullanıcıya yönelik nihai yanıt metni
2) TERMINATE: reason=<completed | awaiting_user_input | escalation_needed | not_found | error>
```

TERMINATE marker'ı kullanıcıya **yansıtılmaz** — `WorkflowResponseExtractor.RemoveTerminationMarkers` ve `RemoveTechnicalJsonBlocks` tarafından temizlenir.

### Escalation farkındalığı

Specialist mesajlarının `postToolReflection.status` alanına bakarak reason'u doğru seçmesi beklenir:

| Specialist status | Response reason |
|---|---|
| `needs_escalation` | `escalation_needed` |
| `failed` | `error` |
| `partial` | `not_found` |
| `needs_followup` | `awaiting_user_input` |
| `done` | `completed` |

### Compound query yanıtı

Kullanıcı mesajı **birden fazla bağımsız işlem** içeriyorsa (ör. *"ORD-1 nerede ve ORD-2 için şikayet aç"*) ResponseAgent prompt'u (`Prompts/agents/response-agent.md`) şu kuralları uygular:

- Yanıt **maddelenmiş** yazılır: *"1) ORD-1 için ... 2) ORD-2 için ..."*
- Her alt görev sonucu ayrı paragrafta özetlenir.
- Tek bir işlem çalıştıysa ama iki istenmişse, **çalışmayan için de bir satır** eklenir: *"İkinci talebiniz (X) için lütfen ayrı bir mesaj yazın."*
- `TERMINATE: reason=completed` yine **tek sefer**, yanıtın en sonunda olur.

Ancak pratikte `CustomerSupportTeam.RunDecomposedAsync` her subtask için **ayrı** bir workflow run çalıştırdığı için ResponseAgent her run'da kendi subtask sonucunu tek başına üretir. `CustomerSupportTeam` bu parçaları `JoinAggregatedParts` ile `\n\n---\n\n` ayırıcılı tek bir metin halinde birleştirir ve başlık olarak `**{order}) {description}**` ekler. Yani gerçek maddelenmiş format **orkestrasyon katmanında** oluşur; ResponseAgent prompt kuralı, tek-workflow fallback senaryosu için bir güvencedir.

---

## Ajanlar arası handoff grafiği

```
  [Tek-görev akışı]                    [Compound query]
                                       
                                       ┌──────────────────────────┐
                                       │ CustomerSupportTeam      │
                                       │  RunDecomposedAsync      │  (kod katmanı)
                                       │  / Streaming versiyonu   │
                                       └─────────┬────────────────┘
                                                 │ her subtask ayrı
                                                 │ workflow run  
                                                 ▼
    ┌──────────────────┐            ┌──────────────────┐
    │ PlanningAgent    │ (Turun başı)│ PlanningAgent    │ (her subtask için)
    └───┬──────────────┘            └───┬──────────────┘
        │                                │
┌───────┼─────────┬────────────┬─────────┼──────────┐
│       │         │            │         │          │
▼       ▼         ▼            ▼         ▼          ▼
┌──────────┐  ┌──────────────────┐  ┌─────────────┐  ┌──────────────┐
│ Product  │  │ OrderPlacement   │  │ OrderInquiry│  │ Complaint    │
│ Inquiry  │  │ (yan etkili)     │  │ (read-only) │  │ (yan etkili) │
└─────┬────┘  └─────┬────────────┘  └──────┬──────┘  └──────┬───────┘
      │             │                       │                │
      │             │  dinamik handoff      │                │
      │◀────────────┘                       │                │
      │             ◀──────────────────────┘                │
      │             ◀───────────────────────────────────────┘
      │
      │ (hepsi en sonunda ResponseAgent'a düşer —
      │  dinamik handoff sayacı max 2, sonra zorla ResponseAgent)
      ▼
   ┌──────────────────────┐
   │ ResponseAgent        │ (Turun sonu → TERMINATE)
   └──────────────────────┘
                                                 │
                                                 │ her subtask için tekrar
                                                 ▼
                                       ┌──────────────────────────┐
                                       │ CustomerSupportTeam      │
                                       │  JoinAggregatedParts(...)│
                                       │  → "1) ... --- 2) ..."   │
                                       └──────────────────────────┘
```

Dinamik handoff mekaniği ve ping-pong guard için → [workflow.md](workflow.md). Compound query orkestrasyonu için → [reasoning.md#compound-query](reasoning.md) ve [workflow.md#compound-query-orkestrasyonu](workflow.md).

## Prompt kaynakları

Tüm ajan instruction'ları `Prompts/agents/` altında versiyonlanır. Kod içi inline prompt **yasaktır** — `PromptService.Get("agents/<name>")` çağrısı kullanılır. Detay → [developer-guide.md#yeni-ajan-ekleme](developer-guide.md#yeni-ajan-ekleme).

---

## Agent-level OpenTelemetry

Tüm ajanlar MAF'ın `.UseOpenTelemetry()` middleware'i ile sarılarak **otomatik agent/tool/LLM span'ları** üretir. Bu, `CustomerSupportTelemetry` içindeki manuel `StartAgentActivity`/`StartToolActivity` helper'larına **ek** olarak çalışır — çakışmaz, span isimleri farklıdır.

### Kurulum

`@Agents/CustomerSupportTeam.cs` ajanları tek bir helper'dan geçirir:

```csharp
var sourceName = CustomerSupportTelemetry.ActivitySourceName; // "CustomerSupportBot"

_planningAgent = WrapWithTelemetry(new ChatClientAgent(
    chatClient, instructions: ..., name: WellKnown.AgentNames.Planning, ...), sourceName);

// ... diğer 6 agent aynı pattern'de ...

private static AIAgent WrapWithTelemetry(ChatClientAgent agent, string sourceName)
    => agent.AsBuilder().UseOpenTelemetry(sourceName).Build();
```

Önemli detay: alan tipleri `ChatClientAgent` → **`AIAgent`**'a yükseltildi, çünkü middleware sarmalanmış agent concrete tipi korumaz. `AddParticipants(params AIAgent[])` çağrısı etkilenmez.

### Span ağacı

Her kullanıcı turunun trace'i aşağıdaki gibi görünür (OTLP collector → Jaeger/Tempo/Aspire Dashboard):

```
http POST /chat/stream                               (ASP.NET instrumentation)
 └─ ai.reasoning                                     (manual: ReasoningService)
 └─ workflow.run                                     (manual: CustomerSupportTeam)
    ├─ agent.run PlanningAgent                       ← MAF middleware
    │   └─ chat.completions                          ← inner IChatClient span
    ├─ agent.run OrderInquiryAgent                   ← MAF middleware
    │   ├─ chat.completions
    │   └─ tool.invoke order_status_tool             ← MAF middleware
    │       └─ DB query (EF Core instrumentation)
    └─ agent.run ResponseAgent
        └─ chat.completions
```

Manuel span (`agent.PlanningAgent`) ve middleware span (`agent.run PlanningAgent`) yan yana görünür — ilki **domain-level** (session tag'leri, trace ID), ikincisi **protocol-level** (token sayısı, model, latency, finish reason).

### Aynı source, aynı exporter

`AddSource("CustomerSupportBot")` `TracerProviderBuilder`'da zaten kayıtlı olduğu için middleware span'ları **otomatik** toplanır — ek konfigürasyon yoktur. Bkz. `@Extensions/TelemetryExtensions.cs`.

### Neden böyle?

- **Sıfır manuel enstrümantasyon**: Her yeni ajan `WrapWithTelemetry` çağrısıyla ücretsiz trace/metric kazanır
- **Standart semantic convention'lar**: MAF span isimleri OpenAI/GenAI semantic conventions'a uygun — Jaeger/Tempo'da filtrelenebilir
- **Sesli mod ile tutarlılık**: Sesli konuşmalar aynı ajanlardan geçer → [realtime.md](realtime.md)

