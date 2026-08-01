# Developer Guide

Bu doküman geliştiricilerin en sık ihtiyaç duyacağı iş senaryolarını **adım adım** anlatır. Her başlıkta:

1. Ne yapmak istiyorum?
2. Hangi dosyalara dokunulur?
3. Örnek kod
4. Dikkat edilecekler

## İçindekiler

1. [Geliştirme ortamı](#geliştirme-ortamı)
2. [Yeni prompt ekleme](#yeni-prompt-ekleme)
3. [Yeni tool ekleme](#yeni-tool-ekleme)
4. [Yeni agent ekleme](#yeni-agent-ekleme)
5. [Yeni context provider ekleme](#yeni-context-provider-ekleme)
6. [Yeni endpoint ekleme](#yeni-endpoint-ekleme)
7. [Yeni evaluation senaryosu ekleme](#yeni-evaluation-senaryosu-ekleme)
8. [Yeni sanity checker kuralı ekleme](#yeni-sanity-checker-kuralı-ekleme)
9. [Yeni entity tipi ekleme (EntityVerifier)](#yeni-entity-tipi-ekleme-entityverifier)
10. [Compound query senaryosunu test etme](#compound-query-senaryosunu-test-etme)
11. [Hata ayıklama rehberi](#hata-ayıklama-rehberi)
12. [Yaygın tuzaklar](#yaygın-tuzaklar)

---

## Geliştirme ortamı

**Gereksinimler**:
- .NET 10 SDK
- Aşağıdaki AI sağlayıcılardan **biri** için erişim:
  - **OpenAI** (chat + o-series reasoning)
  - **Azure OpenAI** (`gpt-5.1` deployment + reasoning deployment)
  - **Anthropic** (Claude — Sonnet/Opus/Haiku)

**Sağlayıcı seçimi**: `appsettings.json > AI:Provider` (`OpenAI` | `AzureOpenAI` | `Anthropic`). Yapılandırma sınıfı: `CustomerSupportBot.Adapters.AI/Options/AiProviderOptions.cs`.

**Çalıştırma**:
```bash
# Gizli API key (kullanılan sağlayıcıya göre birini seçin)
cd CustomerSupportBot.Api
dotnet user-secrets init

# OpenAI
dotnet user-secrets set "AI:Provider"           "OpenAI"
dotnet user-secrets set "AI:OpenAI:ApiKey"      "sk-..."

# Azure OpenAI
dotnet user-secrets set "AI:Provider"               "AzureOpenAI"
dotnet user-secrets set "AI:AzureOpenAI:Endpoint"   "https://<resource>.openai.azure.com"
dotnet user-secrets set "AI:AzureOpenAI:ApiKey"     "..."
# Deployment isimleri appsettings.json'dan okunur (default: gpt-5.1)

# Anthropic
dotnet user-secrets set "AI:Provider"           "Anthropic"
dotnet user-secrets set "AI:Anthropic:ApiKey"   "sk-ant-..."

# Sunucu
dotnet run --project CustomerSupportBot.Api
# → http://localhost:5021
```

**Build:**
```bash
dotnet build CustomerSupportBot.Api/CustomerSupportBot.Api.csproj -nologo -v q
# veya tüm çözüm:
dotnet build CustomerSupport.slnx -nologo -v q
```

**Temel kontrol**:
- `/` → frontend chat UI
- `POST /chat/` → non-streaming chat
- `POST /chat/stream` → SSE streaming
- `GET /traces/recent` → son trace'ler
- `POST /eval/run` → senaryoları koştur (`GET /eval/scenarios` ile mevcut senaryolar listelenir)

---

## Yeni prompt ekleme

### Senaryo

"ResponseAgent'a yeni bir `hata-mesaji.md` prompt'u eklemek istiyorum" veya "mevcut prompt'a bir `{{YENI_PLACEHOLDER}}` ekleyip kodda besleyeceğim".

### Adımlar

**1. MD dosyasını oluştur:**

```bash
# Prompt kategorisine göre alt klasör seç
# - agents/   → ChatClientAgent instructions
# - services/ → domain service prompt'ları
mkdir -p Prompts/services
```

```
Prompts/services/hata-mesaji.md
```

```md
Müşteri destek konusunda samimi bir hata mesajı üret.

Hata türü: {{ERROR_TYPE}}
Kullanıcı kimliği: {{CUSTOMER_ID}}

Türkçe yaz, en fazla 2 cümle.
```

**2. `CustomerSupportBot.Api.csproj` zaten `Prompts/**/*.md`'yi `PreserveNewest` ile kopyalıyor** — ekstra kayıt gereksiz.

**3. Kodda kullan:**

```csharp
// Basit get (placeholder yoksa)
var text = _prompts.Get("services/hata-mesaji");

// Placeholder'lı render
var text = _prompts.Render("services/hata-mesaji", new Dictionary<string, string?>
{
    ["ERROR_TYPE"] = "TIMEOUT",
    ["CUSTOMER_ID"] = session.State.CustomerId ?? "bilinmiyor"
});
```

### Dikkat edilecekler

- **Anahtar formatı**: Dosya yolu `Prompts/` sonrası, `.md` uzantısız, `/` separator ile. `Prompts/services/foo.md` → `"services/foo"`.
- **`{{PLACEHOLDER}}` syntax'ı**: Sadece `[A-Za-z0-9_]` karakterleri desteklenir. Boşluk tolere edilir: `{{ ANAHTAR }}` da çalışır.
- **Bulunmayan placeholder**: Boş string ile değiştirilir — `null` atma riskin yok.
- **`README.md` ve `NOTES.md`** loader tarafından atlanır; bunları dokümantasyon için kullanabilirsiniz.
- **Build**: Prompt değişikliği için `dotnet build` yeterli; kod değişmediğinden rebuild hızlıdır (`PreserveNewest`).

### Prompt dizin konvansiyonu

```
CustomerSupportBot.Api/Prompts/
├── README.md              # Loader atlar
├── agents/                # ChatClientAgent instructions
│   └── <agent-name>.md
└── services/              # Service-level prompt'lar
    ├── <service>-system.md   # System message
    ├── <service>-user.md     # User message template
    └── <feature>.md          # Diğerleri
```

---

## Yeni tool ekleme

### Senaryo

"`RefundInitiateTool` adında bir iade başlatma aracı eklemek istiyorum."

### Adımlar

**1. `CustomerSupportToolsService.cs`'e metod ekle**:

```csharp
// CustomerSupportBot.Application/Services/CustomerSupportToolsService.cs
[Description("Sipariş için iade süreci başlatır. orderId ve reason zorunlu. " +
             "Sonuç ToolResult olarak döner.")]
public ToolResult RefundInitiateTool(
    [Description("İade edilecek sipariş numarası (ör. '1')")] string orderId,
    [Description("İade sebebi (en az 10 karakter)")] string reason)
{
    // 1) Validation — parametre adları için WellKnown.ToolParameterNames kullan
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(orderId)) missing.Add(WellKnown.ToolParameterNames.OrderId);
    if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10) missing.Add(WellKnown.ToolParameterNames.Reason);
    if (missing.Count > 0)
    {
        return ToolResult.ValidationError(
            $"İade için şu bilgiler gerekli: {string.Join(", ", missing)}",
            missing.ToArray());
    }

    // 2) Business logic — IOrderRepository port'u üzerinden erişim
    var order = _orderRepository.FindOrder(orderId);
    if (order == null)
    {
        return ToolResult.NotFound(
            WellKnown.ToolErrorCodes.OrderNotFound,
            $"'{orderId}' siparişi bulunamadı.");
    }

    // 3) Success
    return ToolResult.Ok(
        message: $"İade süreci başlatıldı: {orderId}",
        data: new { orderId, reason, status = "İadeBekliyor" });
}
```

> **Konvansiyon**: Yeni tool yazarken parametre adları (`order_id`, `customer_id`, vb.) `WellKnown.ToolParameterNames`'e, yeni error code'lar (`ORDER_NOT_FOUND`, vb.) `WellKnown.ToolErrorCodes`'a eklenmeli. Hard-coded magic string yerine sabit kullan — frontend ve test kodu bu sabitleri ortak referans olarak kullanır.

**2. İlgili agent prompt'unda tool'u dokümante et**:

```md
<!-- CustomerSupportBot.Api/Prompts/agents/complaint-agent.md -->
...
TOOL'LAR:
  - complaint_registration_tool : Şikayet kaydı (yan etkili)
  - refund_initiate_tool        : İade süreci başlat (yan etkili, NEW)

...
REFUND İÇİN:
  - requiredParams: ["order_id", "reason"]
  - reason en az 10 karakter olmalı
  - refund_initiate_tool'u çağır
```

**3. Agent'ın `tools:` listesine ekle**:

```csharp
// CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs
var complaintAgent = new ChatClientAgent(
    chatClient,
    instructions: _prompts.Get("agents/complaint-agent"),
    name: "ComplaintAgent",
    description: "Müşteri şikayetlerini işler.",
    tools: [
        AIFunctionFactory.Create(_tools.ComplaintRegistrationTool),
        AIFunctionFactory.Create(_tools.RefundInitiateTool)  // ← eklendi
    ]);
```

**4. Evaluation senaryosu ekle** (isteğe bağlı ama tavsiye edilir):

```yaml
# docs/evaluation-scenarios.yaml
- id: "refund-happy-path"
  category: "şikayet"
  query: "1001 için iade açmak istiyorum, ürün arızalı geldi."
  expected_tools: ["refund_initiate_tool"]
  success_criteria:
    - "response contains 'iade'"
    - "refund_initiate_tool called"
```

### Dikkat edilecekler

- **`ToolResult` zarfı zorunlu**: Doğrudan `string`/`object` dönmeyin. Factory'ler: `Ok`, `ValidationError`, `NotFound`, `Conflict`, `SystemError`.
- **`[Description]` attribute'ları** LLM'in gördüğü schema'yı belirler — açık, hatalara neden olmayacak şekilde yazın.
- **Yan etkili tool'larda** `lock` kullanın (`_stockLock` gibi) — concurrent request'lerde race condition olur.
- **ID formatı konsistansı**: Yeni ID türleri eklerseniz `IdExtractor`'a regex ekleyin ki entity extraction hint'i devreye girebilsin.
- **Error code naming**: `SNAKE_UPPER` (ör. `REFUND_ALREADY_OPEN`). Category: `validation | not_found | conflict | business_rule | system`.

---

## Yeni agent ekleme

> **Detaylı referans:** Tüm `Adapters.Agents` sınıflarının kapsamlı dokümantasyonu için [`docs/adapters-agents/`](adapters-agents/README.md) klasörüne, `Application` katmanı için [`docs/application/`](application/README.md) klasörüne bakın.

### Senaryo

"`BillingAgent` adında fatura işlemleri için yeni bir specialist eklemek istiyorum."

### Adımlar

**1. Prompt dosyası oluştur** (`CustomerSupportBot.Api/Prompts/agents/billing-agent.md`):

```md
Sen BillingAgent'sın. Fatura işlemleri için `fetch_invoice_tool` kullanırsın.

TOOL RESULT ZARFI:
fetch_invoice_tool {success, confidence, message, data, error} döner.
  - success=true → data.invoiceUrl kullan
  - error.code=INVOICE_NOT_FOUND → status=partial

GEREKLİ PARAMETRELER:
  - order_id : Faturası istenen sipariş (zorunlu)

ADIMLAR:
1) ```json { "preToolCheck": {...} } ``` bloğu üret
2) canProceed=true ise tool çağır
3) Tool sonrası postToolReflection üret
4) Türkçe kısa mesaj yaz

JSON ŞEMASI:
```json
{
  "preToolCheck": { "requiredParams": ["order_id"], "canProceed": ..., ... },
  "resultConfidence": ...,
  "resultNotes": "...",
  "postToolReflection": { "status": "done|partial|...", "handoffSuggestion": "ResponseAgent" }
}
```

HANDOFF KURALLARI:
- Fatura bulundu → done / ResponseAgent
- Bulunamadı → partial / ResponseAgent
```

**2. Agent'ı `CustomerSupportTeam` ctor'da yarat**:

```csharp
// CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs

// 7. BillingAgent — yeni specialist
var billingAgent = new ChatClientAgent(
    chatClient,
    instructions: _prompts.Get("agents/billing-agent"),
    name: "BillingAgent",
    description: "Fatura sorgularını işler.",
    tools: [AIFunctionFactory.Create(_tools.FetchInvoiceTool)]);
```

**3. Workflow'a ekle**:

```csharp
_workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(...)
    .AddParticipants(
        planningAgent,
        productAgent,
        orderAgent,
        complaintAgent,
        billingAgent,  // ← yeni
        responseAgent)
    .Build();
```

**4. `PlanningAgent` prompt'unu güncelle** (`CustomerSupportBot.Api/Prompts/agents/planning-agent.md`):

```md
MEVCUT AJANLAR:
  - ProductAgent : Ürün soruları
  - OrderAgent          : Sipariş oluşturma + durum/geçmişi
  - ComplaintAgent      : Şikayet kaydı
  - BillingAgent        : Fatura sorguları ← yeni
  - ResponseAgent       : Kullanıcıya final yanıt
```

**5. `CustomerSupportChatManager` specialist listelerine ekle**:

```csharp
// CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs
private static readonly string[] SpecialistPrefixes =
{
    "ProductAgent",
    "OrderAgent",
    "ComplaintAgent",
    "BillingAgent"  // ← eklendi
};
```

**6. `IsInternalRoutingMessage` listesini güncelle** (`CustomerSupportTeam.cs:860-864`).

**7. Specialist extraction listesini güncelle** (`ExtractSpecialistReasoningsFromOutput`, `CustomerSupportTeam.cs:792-798`).

**8. `EvaluationRunner.AgentToToolName`** mapping'i güncelle.

**9. Evaluation senaryoları ekle**.

### Checklist

- [ ] `CustomerSupportBot.Api/Prompts/agents/<name>.md` oluşturuldu
- [ ] `ChatClientAgent` `CustomerSupportTeam` ctor'da
- [ ] `AddParticipants(...)` listesinde
- [ ] PlanningAgent prompt'unda bahsedildi
- [ ] `SpecialistPrefixes` listesine eklendi
- [ ] `IsInternalRoutingMessage` listesine eklendi
- [ ] `ExtractSpecialistReasoningsFromOutput` listesine eklendi
- [ ] `EvaluationRunner.AgentToToolName` mapping var
- [ ] Evaluation senaryoları yazıldı

### Dikkat edilecekler

- **Agent kimliği kritik**: Mesajın `AuthorName`'i doğru set edilmeli — MAF `ChatClientAgent.name` parametresinden bunu türetir. Specialist olarak tanınması için prefix match önemli.
- **JSON şeması**: `preToolCheck` + `postToolReflection` üretmeli. `SpecialistReasoningParser` bu iki alanı bekler.
- **Handoff hedefleri**: Prompt'ta `handoffSuggestion` için kullanılacak ajan adlarını net yaz — `CustomerSupportChatManager.cs:137-138` `StartsWith` ile eşleştirir.

---

## Yeni context provider ekleme

### Senaryo

"Müşterinin sadakat puanı bilgisini CRM'den çekip prompt'a ekleyeceğim."

### Adımlar

**1. `IContextProvider` implementasyonu**:

```csharp
// CustomerSupportBot.Application/Services/Providers/LoyaltyContextProvider.cs
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public class LoyaltyContextProvider : IContextProvider
{
    public string Name => "LoyaltyContext";
    public int Order => 20;  // CustomerContextProvider'dan (10) sonra

    public async Task<string?> GetContextAsync(AgentSession session)
    {
        var customerId = session.State.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return null;

        // CRM çağrısı veya cache
        var loyalty = await FetchLoyaltyAsync(customerId);
        if (loyalty == null) return null;

        return $"[Sadakat Bilgisi]\n" +
               $"Seviye: {loyalty.Tier}\n" +
               $"Puan: {loyalty.Points}";
    }

    private Task<LoyaltyInfo?> FetchLoyaltyAsync(string customerId) { ... }
}
```

**2. `ApplicationServicesExtensions.cs`'de kaydet**:

```csharp
// CustomerSupportBot.Api/Extensions/ApplicationServicesExtensions.cs
services.AddSingleton<IContextProvider, CustomerContextProvider>();
services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
services.AddSingleton<IContextProvider, LoyaltyContextProvider>();  // ← yeni
```

**3. Test et**: Bir oturumda `customerId` set edildiğinde prompt'ta `[Sadakat Bilgisi]` bölümünün göründüğünü `/traces/{id}` ile kontrol edebilirsiniz.

### Dikkat edilecekler

- **`Order` değeri kritik**: Düşük = yüksek öncelik. `ConversationSummaryProvider`=5, `CustomerContextProvider`=10. Yeni provider'lar genelde 15-50 arası.
- **Null dönmek OK**: Context yoksa `null` döndürün, pipeline geçecektir.
- **Exception'lar yutulur**: `ContextPipeline` her provider'ı try-catch'te çalıştırır, hata olursa log atar ve devam eder. Yine de gereksiz exception atmaktan kaçının.
- **Singleton scope**: Provider'lar singleton. Dış servis çağrısı yapıyorsa `IHttpClientFactory` gibi thread-safe client'lar kullanın.

---

## Yeni endpoint ekleme

### Senaryo

"Admin panelinden toplu session silme endpoint'i istiyorum."

### Adımlar

**1. Extension metod ekle** (örneğin `SessionEndpoints.cs`'e):

```csharp
// CustomerSupportBot.Api/Endpoints/SessionEndpoints.cs
app.MapDelete("/sessions/purge-old", (int daysOld, ISessionManager sessionRepo) =>
{
    var cutoff = DateTime.UtcNow.AddDays(-daysOld);
    var removed = mgr.PurgeOlderThan(cutoff);
    return Results.Json(new { removedCount = removed });
});
```

**2. (Varsa) yeni endpoint grubu için ayrı dosya**:

```csharp
// CustomerSupportBot.Api/Endpoints/AdminEndpoints.cs
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/flush-traces", ...);
        return app;
    }
}
```

**3. `Program.cs`'de mapla**:

```csharp
app.MapChatEndpoints();
app.MapSessionEndpoints();
app.MapTraceEndpoints();
app.MapEvaluationEndpoints();
app.MapAdminEndpoints();  // ← yeni
```

### Dikkat edilecekler

- **CORS**: `Program.cs:15-21` zaten `AllowAnyOrigin` — production'da kısıtlamak gerekir.
- **Auth**: Şu an yok; admin endpoint'leri için en azından API key / basic auth eklenmeli.
- **SSE**: Streaming endpoint için `SseWriter` helper'ını kullanın.

---

## Yeni evaluation senaryosu ekleme

### Senaryo

"`BillingAgent`'ın doğru fatura getirdiğini test etmek istiyorum."

### Adımlar

**1. `docs/evaluation-scenarios.yaml`'a ekle**:

```yaml
- id: "billing-happy-path"
  category: "fatura"
  query: "1001 için faturamı gönderir misiniz?"
  expected_intent: "fatura"
  expected_agents: ["PlanningAgent", "BillingAgent", "ResponseAgent"]
  expected_tools: ["fetch_invoice_tool"]
  success_criteria:
    - "response contains 'fatura'"
    - "fetch_invoice_tool called"
    - "turn_count <= 10"
```

`success_criteria` değerleri düz İngilizce metin satırlarıdır — `type:`/`value:` içeren YAML nesneleri desteklenmez. `CriteriaEvaluator.Evaluate` bu metinleri regex ile yorumlar.

**2. Çalıştır**:

```bash
curl -X POST "http://localhost:5021/eval/run" \
  | jq '.results[] | select(.scenarioId == "billing-happy-path")'
```

Tekil senaryo çalıştırmak için: `POST /eval/run/billing-happy-path`

### Desteklenen criterion type'ları

`CustomerSupportBot.Adapters.Agents/Evaluation/CriteriaEvaluator.cs` okuyarak desteklenen `type`'lara bakabilirsiniz (bkz. [evaluation.md §3](evaluation.md#3-criteriaevaluator--yapılandırılmış-typed-criterion-dispatch)). Yeni bir `type` eklemek için `Checks` dispatch table'ına yeni bir entry eklenir — `Microsoft.Agents.AI.EvalCheck` döndüren bir `Func<CriterionSpec, ScenarioRunContext, EvalCheck>`.

---

## Yeni sanity checker kuralı ekleme

### Senaryo

"Reasoning `intentConfidence >= 0.9` ama hiç `supportingEvidence` yoksa uyarı üret" gibi 9. bir kural eklemek.

### Adımlar

**1. `IReasoningSanityRule` implementasyonu olarak yeni sınıf ekle**:

Sanity checker artık **strategy pattern** kullanır — her kural ayrı bir sınıftır. `CustomerSupportBot.Application/Services/ReasoningSanityChecker.cs` dosyasının sonuna ekle:

```csharp
// CustomerSupportBot.Application/Services/ReasoningSanityChecker.cs

/// <summary>Yüksek confidence + boş supportingEvidence çelişkisi.</summary>
public sealed class OverconfidentWithoutEvidenceRule : IReasoningSanityRule
{
    public string Code => "confident_without_evidence";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (r.ConfidenceScore < 0.9) return;
        if (r.SupportingEvidence?.Count > 0) return;

        issues.Add(new ReasoningIssue
        {
            Code = Code,
            Severity = IssueSeverity.Warn,
            Message = $"confidenceScore={r.ConfidenceScore:F2} ama supportingEvidence boş. " +
                      "Kanıt eklenmeden yüksek güven şüpheli.",
            Field = "supportingEvidence",
            SuggestedFix = "En az 1 supportingEvidence ekle veya confidence'ı düşür."
        });
    }
}
```

**2. `ReasoningSanityChecker` constructor'ındaki `_rules` listesine ekle**:

```csharp
public ReasoningSanityChecker(ILogger<ReasoningSanityChecker> logger)
{
    _logger = logger;
    _rules =
    [
        new OverconfidentClarificationRule(),
        new RedundantRequiredInfoRule(),
        // ... mevcut 8 kural ...
        new SubTasksIgnoredRule(),
        new OverconfidentWithoutEvidenceRule(),  // ← eklendi
    ];
}
```

`Check` metoduna dokunmaya gerek yok — yeni kural `_rules` listesine eklendiği anda otomatik olarak çalışır. Hata olursa `try/catch` bloğu kuralı atlar ve diğerleri çalışmaya devam eder.

**3. (Opsiyonel) Frontend'de ileri kod eşlemesi**:

Frontend (`wwwroot/js/chat-ui.js`) issue code'larını doğrudan render eder — generic. Ama istiyorsan code'a özel stil/ikon eklemek için:

```javascript
// buildReasoningPanel > reasoning-issues section
const severityIcon = {
    error: "⛔",
    warn: "⚠️",
    info: "ℹ️",
    confident_without_evidence: "🤔"   // özel ikon (opsiyonel)
}[issue.code] || ...;
```

### Kural tasarım prensipleri

- **Deterministic olmalı** — LLM çağırma. Amacımız sıfır ek maliyet.
- **`VerifiedEntities`'i kullanmaktan kork ma** — entity grounding sonucu sağlıklı veri.
- **Severity seçimi**:
  - `Error` → gerçek hallucination veya ping-pong riski (ileride otomatik re-prompt tetikleyebilir).
  - `Warn` → şüpheli durum, tasarlanmamış davranış (loglanır, UI'da görünür).
  - `Info` → debugging için bilgi.
- **`SuggestedFix` yaz** — debug sırasında neyi değiştirmesi gerektiğini gösterir.

### Test

Reasoning'i bu duruma sokacak bir evaluation senaryosu ekle:

```yaml
# docs/evaluation-scenarios.yaml
- id: "sanity-check-overconfident-no-evidence"
  query: "..."   # reasoning'i yukarıdaki duruma sokacak input
  success_criteria:
    - "response contains 'bilgi'"   # manuel doğrulama gerektirir
```

Not: `trace_field` tipi mevcut `CriteriaEvaluator`'da desteklenmez. Sanity issue tespiti için `EvaluationRunner.RunScenarioAsync` metodunu genişletmek veya doğrudan trace API'sini (`GET /traces/recent`) kontrol etmek gerekir.

Detay → [domain/Model-Reasoning.md](domain/Model-Reasoning.md).

---

## Yeni entity tipi ekleme (EntityVerifier)

### Senaryo

"Kampanya kimliği (`CMPG-XXX`) eklemek ve reasoning'e hint olarak geçirmek istiyorum."

### Adımlar

**1. `IdExtractor.cs`'e regex ekle**:

```csharp
// CustomerSupportBot.Domain/Services/IdExtractor.cs
private static readonly Regex CampaignIdPattern =
    new(@"\bCMPG[-_\s]?(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

public static ExtractedIds Extract(string text)
{
    // mevcut order/customer/complaint çıkarımı
    var campaignMatch = CampaignIdPattern.Match(text);
    var campaignId = campaignMatch.Success ? $"CMPG-{campaignMatch.Groups[1].Value}" : null;
    // ...
}
```

**2. `VerifiedEntities` model'ine alan ekle**:

```csharp
// CustomerSupportBot.Domain/Model/VerifiedEntities.cs
public class VerifiedEntities
{
    public VerifiedEntity? OrderId { get; set; }
    public VerifiedEntity? CustomerId { get; set; }
    public VerifiedEntity? ComplaintId { get; set; }
    public VerifiedEntity? CampaignId { get; set; }   // ← yeni
    // ...
}
```

**3. `EntityVerifier.Verify` içinde port lookup**:

```csharp
// CustomerSupportBot.Application/Services/EntityVerifier.cs
// Önce IProductCatalogRepository / IOrderRepository gibi uygun bir port'u enjekte edin
if (!string.IsNullOrWhiteSpace(ids.CampaignId))
{
    var campaign = _campaignRepository?.FindCampaign(ids.CampaignId);
    if (campaign != null)
    {
        verified.CampaignId = new VerifiedEntity
        {
            Value = ids.CampaignId,
            Source = EntitySource.Query,
            Verification = EntityVerification.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["discount"] = $"{campaign.DiscountPercent}%",
                ["valid_until"] = campaign.ValidUntil.ToString("yyyy-MM-dd")
            }
        };
    }
    else
    {
        verified.CampaignId = new VerifiedEntity
        {
            Value = ids.CampaignId,
            Source = EntitySource.Query,
            Verification = EntityVerification.NotFoundInDb
        };
    }
}
```

**4. `BuildPromptBlock` — prompt enjeksiyonuna ekle**:

```csharp
// EntityVerifier.BuildPromptBlock yardımcısı
if (verified.CampaignId?.Verification == EntityVerification.Verified)
{
    var meta = string.Join(", ", verified.CampaignId.Metadata.Select(kv => $"{kv.Key}={kv.Value}"));
    sb.AppendLine($"- campaign_id = \"{verified.CampaignId.Value}\" [VERIFIED, {meta}]");
}
```

**5. (Opsiyonel) Yeni sanity kuralı** — kampanya bulunamadığı halde indirim vaat edilmesi gibi durumlar için yeni `ReasoningSanityChecker` kuralı eklenebilir.

### Checklist

- [ ] `IdExtractor` regex eklendi
- [ ] `VerifiedEntities` modelı güncellendi
- [ ] `EntityVerifier.Verify` DB lookup yapıyor
- [ ] `BuildPromptBlock` prompt'a enjekte ediyor
- [ ] `reasoning-system.md` yeni entity tipi hakkında not ekleniyor (opsiyonel ama önerilir)
- [ ] Evaluation senaryosu yazıldı

Detay → [domain/Model-Reasoning.md](domain/Model-Tools.md).

---

## Compound query senaryosunu test etme

### Senaryo

Compound query orkestrasyonunun 2+ subtask'lı bir sorguda doğru çalıştığını doğrulamak.

### Adımlar

**1. `docs/evaluation-scenarios.yaml`'a senaryo ekle**:

```yaml
- id: "compound-order-and-complaint"
  category: "compound"
  query: "1 siparişim nerede ve 2 için şikayet açmak istiyorum, ürün arızalı geldi"
  expected_intent: "compound"
  expected_agents: ["PlanningAgent", "OrderAgent", "ResponseAgent", 
                    "PlanningAgent", "ComplaintAgent", "ResponseAgent"]
  expected_tools: ["order_status_tool", "complaint_registration_tool"]
  success_criteria:
    - "response contains '1)'"         # maddelenmiş format
    - "response contains '2)'"
    - "order_status_tool called"
    - "complaint_registration_tool called"
    - "turn_count <= 15"
```

**2. Local test**:

```bash
cd CustomerSupportBot
dotnet run
# → http://localhost:5021
```

Chat UI'dan şu sorguyu deneyin:

> *"1 nerede ve 2 için şikayet açmak istiyorum"*

**3. Gözlemler**:

- **Reasoning panelinde** "Alt görevler" bölümü görünmeli — 2 subtask listesi.
- **Agent indicator sırası**:
  ```
  Orchestrator (decomposing, subTaskCount=2)
  SubTask#1 (running, targetAgent=OrderAgent)
    ├─ PlanningAgent
    ├─ OrderAgent
    └─ ResponseAgent
  SubTask#1 (done)
  SubTask#2 (running, targetAgent=ComplaintAgent)
    ├─ PlanningAgent
    ├─ ComplaintAgent
    └─ ResponseAgent
  SubTask#2 (done)
  Orchestrator (aggregating)
  ```
- **Final yanıt** maddelenmiş biçimde olmalı: *"**1) 1 için ...**\n\n...\n\n---\n\n**2) 2 için ...**"*.

**4. Trace inceleme**:

```bash
# Son 5 trace'i al (her subtask ayrı trace)
curl -s http://localhost:5021/traces/recent?count=5 \
  | jq '.[] | { traceId, userQuery, terminationReason }'

# subTasks alanını gör
# En son parent trace — kullanıcı query + reasoning
curl -s http://localhost:5021/traces/recent?count=1 \
  | jq '.[0].reasoning.subTasks'
```

### Sınama kontrolleri

| Kontrol | Beklenen |
|---|---|
| `reasoning.subTasks.length` | 2 (veya daha fazla) |
| `reasoning.sanityIssues` içinde `subtasks_ignored` | **yok** (decomposition doğru yapılmış) |
| Session'da N+1 trace (1 parent şeffaf + N subtask) | `/traces/by-session/{sid}` ile görülebilir |
| Response final'da `TERMINATE` | yok (`JoinAggregatedParts` zaten temiz) |
| Frontend'de `response_complete` event'inde `decomposed=true` | evet |

Detay → [domain/Model-Reasoning.md](domain/Model-Reasoning.md).

---

## Hata ayıklama rehberi

### "Ajan aynı soruyu tekrar tekrar soruyor (ping-pong)"

**Olası sebepler**:
1. Entity extraction çalışmıyor — `IdExtractor` log'una bak
2. PlanningAgent prompt'u "çoklu eksik bilgi tek mesajda" kuralını tutmuyor
3. Specialist `handoffSuggestion` loop yapıyor

**Kontrol**:
```bash
# Trace'i aç
curl http://localhost:5021/traces/recent?count=5 | jq '.[0]'

# Planning ve specialist reasoning'lere bak:
.planning.needsClarification, .planning.clarificationQuestion
.specialistReasonings[].postToolReflection.handoffSuggestion
```

### "ResponseAgent 'TERMINATE' atmıyor, workflow max_messages_reached ile bitiyor"

**Olası sebep**: ResponseAgent prompt'unda TERMINATE formatı bozuldu.

**Kontrol**: `Prompts/agents/response-agent.md` — `TERMINATE: reason=<...>` format kuralı yerinde mi?

### "Tool sonucu kullanıcıya JSON olarak sızıyor"

**Olası sebep**: `StripTechnicalJsonBlocks` bir pattern'i yakalayamıyor.

**Kontrol**: `CustomerSupportTeam.cs:651-667` — regex'e yeni anahtar eklenmeli mi?

### "Reasoning sürekli confidence=0.3 dönüyor"

**Olası sebep**: `ReasoningService.ReasonAsync` exception fırlatıyor → fallback devreye giriyor.

**Kontrol**: Log'larda `"Reasoning başarısız"` mesajı var mı? O-series / reasoning deployment erişimi yoksa kullanılan sağlayıcıya göre `appsettings.json > AI:OpenAI:ReasoningModel` / `AI:AzureOpenAI:ReasoningDeployment` / `AI:Anthropic:ReasoningModel` alanını mevcut bir modele çekin (örn. `gpt-4o-mini`).

### "Hallucination yakalanmıyor"

**Olası sebep**: `ResponseAgent` prompt'undaki hallucination sıfır tolerans kuralı yeterince sıkı değil.

**Kontrol**: Trace'deki `FinalResponse` alanını specialist tool çıktısıyla karşılaştırın. Eğer specialist'te olmayan veri response'ta varsa, `Prompts/agents/response-agent.md` prompt'unda hallucination kuralını sıkılaştırın ve örnek ekleyin.

### "Compound query'de ikinci görev yapılmıyor" (compound query)

**Olası sebepler**:
1. **ShouldDecompose false dönüyor** — reasoning `subTasks.Count >= 2` değil veya `TargetAgent` alanları aynı.
2. **Reasoning model subTasks üretmiyor** — `reasoning-system.md` decomposition bölümü atlanmış olabilir.
3. **Sanity checker'da `subtasks_ignored` var** — nextAction hepsinden bahsetmiyor (ama bu sadece uyarıdır, orkestrasyonu engellemez).

**Kontrol**:

```bash
# En son trace'in reasoning'ine bak
curl -s http://localhost:5021/traces/recent?count=1 \
  | jq '.[0].reasoning | { subTasks, sanityIssues }'
```

- `subTasks` boş → reasoning prompt'unu güzden geçir (`Prompts/services/reasoning-system.md`).
- `subTasks` dolu ama aynı `targetAgent` → reasoning yanlış ayrıştırıyor; örneklerle düzelt.
- `subTasks` farklı agent'lar ama workflow'da tek specialist kullanıldı → `CustomerSupportTeam.ShouldDecompose` dönüşünü log ile kontrol et (gerekirse bir `_logger.LogInformation` ekle).

### "Reasoning `requiredInfo`'da VERIFIED entity var" (sanity kural 2)

**Olası sebep**: Reasoning prompt'u verified entity'leri okumadı veya öncelikle tekrar istedi.

**Kontrol**: `/traces/{id}.reasoning`'da:
- `sanityIssues[?code=='redundant_required_info']` var mı?
- Reasoning'in system prompt'una bakarak `[VERIFIED ENTITIES]` bloğu gerçekten enjekte edilmiş mi?

**Çözüm**: `EntityVerifier.BuildPromptBlock` çıktısını log'la (veya trace'e ekle); prompt'a gerçekten varıp varmadığını teyit et. `reasoning-system.md`'deki "VERIFIED entity'yi requiredInfo'ya EKLEMEYİN" kuralını daha belirgin yaz.

---

## Yaygın tuzaklar

### 🕳️ `InMemorySessionManager` için iki ayrı singleton kaydı yapmak

`ISessionManager` tek interface olarak hem session yönetimi hem konuşma geçmişini barındırır. InMemory kullanımda `InMemorySessionManager` somut tip önce kaydedilmeli, `ISessionManager` bu instance'a forward edilmelidir.

**Yanlış**:
```csharp
builder.Services.AddSingleton<ISessionManager, InMemorySessionManager>();
// Başka yerde tekrar:
builder.Services.AddSingleton<ISessionManager, InMemorySessionManager>();
// → İKİ farklı instance! Oturum verisi bölünür.
```

**Doğru** (DI kaydı):
```csharp
builder.Services.AddSingleton<InMemorySessionManager>();
builder.Services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<InMemorySessionManager>());
```

### 🕳️ Inline prompt yazmak

Yasak — tüm prompt'lar `Prompts/**/*.md`'de olmalı. Kodda inline string bulursanız taşıyın. Rationale: prompt iteration'ı kod iteration'ından bağımsız yapılabilsin, content writer review'ı gerektirsin.

### 🕳️ Yan etkili tool'da pre-check atlama

`order_placement_tool` veya `complaint_registration_tool` gibi yan etkili tool'larda LLM bazen parametreleri tam toplamadan çağırmak isteyebilir. Agent prompt'unda `preToolCheck` + `canProceed` disiplinini **sıkı tutun**. Tool içinde de ikinci savunma hattı olarak `ValidationError` factory'si var.

### 🕳️ `Interlocked.Increment` atlama

`InMemoryOrderAdapter` / `InMemoryComplaintAdapter` içinde thread-safe `Interlocked.Increment` counter kullanılıyor. Kendi ID generator'ınızda aynı pattern'i kullanın:

```csharp
private static int _counter = 0;
public static string Next() => $"PREFIX-{Interlocked.Increment(ref _counter)}";
```

### 🕳️ Parser imzası değişikliği

`PlanningResult` modeline yeni alan eklerseniz **hem** `Models/PlanningResult.cs` **hem** `Services/PlanningResultParser.cs` güncellemelidir. Aksi takdirde LLM alanı doldurur ama parser görmezden gelir.

Not: `ReasoningResult`'ın yeni alanları (`Steps`, `SubTasks`, `SanityIssues`) `ReasoningService.ParseSteps`, `ParseSubTasks`, ve `ReasoningSanityChecker.Check` tarafından doldurulur. Yeni alan eklerseniz bu üçlüyü eş zamanlı güncelleyin + frontend `chat-ui.js` render'ına da ekleyin.

### 🕳️ Compound query'de recursion loop

`CustomerSupportTeam.RunDecomposedAsync` recursive `RunAsync` çağırır. `DeriveSubReasoning`'te **`SubTasks=[]`** olarak açıkça set edilmelidir; aksi halde iç çağrı tekrar `ShouldDecompose=true` görür ve sonsuz döngü olur. Eğer `DeriveSubReasoning`'e yeni alan eklerken `SubTasks` alanını clone ediyorsanız dikkat.

### 🕳️ Sanity checker kuralları workflow'u durdurmaz

`ReasoningSanityChecker` **sadece işaretleme** yapar — issue severity'si `Error` olsa bile workflow devam eder. Bu kasıtlı bir tasarım: sanity check *bilgi*, *gate* değil. Eğer bir kural ciddi olduğuna inanıyorsanız, ya (1) reasoning promptunu güncelleyerek kuralın tetiklenmesini engelleyin, ya da (2) `CustomerSupportTeam` içinde sanity issue'ya göre workflow'u durduran bir guard ekleyin.

### 🕳️ Recursive `RunAsync` session/trace kirletir

Compound query'de her subtask kendi trace'ini üretir (N trace). Reporting/analitik yaparken tek bir "parent trace" varsaymanızı beklemeyin. `TraceEndpoints.GetBySession(sid)` çağrısında sıralı listedeki son N trace'in aynı compound query'ye ait olduğunu trace metadata'dan çıkarım yapmalısınız (ileride `ReasoningTrace.ParentTraceId` eklenebilir).

### 🕳️ `AuthorName` null gelebiliyor (MAF)

Agent mesajlarının `AuthorName` property'si bazen set olmayabilir. `CustomerSupportChatManager` ve `CustomerSupportTeam` **hem** `AuthorName` kontrolü **hem de** JSON içerik kontrolü yapar:

```csharp
if (lastMessage?.AuthorName == "PlanningAgent" ||
    (lastMessage?.Text?.Contains("\"selectedAgent\"", …) == true))
```

Yeni agent eklerken aynı iki-yönlü kontrolü uygulayın.

### 🕳️ `WorkflowOutputEvent.Data` tip varsayımı

MAF'ın bu alanı bazen `IEnumerable<ChatMessage>`, bazen tek `ChatMessage`, bazen `string` olabilir. `ExtractResultFromOutput` üç tipi de handle eder — yeni extraction metodları yazarken aynı pattern'i takip edin.

### 🕳️ "TERMINATE" dışında arama yapmak

ResponseAgent'ın TERMINATE ile sonlandığını varsayar sistem. Başka "özel marker" eklemek istiyorsanız `ShouldTerminateAsync` + `CleanTerminateMarker` + `ParseTerminationReasonFromResult` üçünü birlikte güncelleyin.

---

## Kod tutumu (coding conventions)

- **Türkçe**: Kod yorumları Türkçe, prompt'lar Türkçe, log mesajları Türkçe, kullanıcı metinleri Türkçe. Kod identifier'ları İngilizce.
- **Nullable**: Proje Nullable açık — `string?`, `T?` anlamlı kullanılmalı.
- **Async**: Tüm I/O `async Task` — `Result` / `Wait()` kullanımı yasak.
- **Single Responsibility**: Yeni tool/servis/provider ayrı dosyada. `CustomerSupportTeam.cs` zaten uzun (~874 satır) — onu daha da büyütmekten kaçının, yardımcıları ayrı dosyaya.
- **Proje gelişim aşamaları**: Kod yorumlarında geçmiş aşamalar belgelenmiştir. Yeni kod ekleyenler mevcut konvansiyonları takip etmelidir.

---

## Faydalı sorgular

```bash
# Son 5 trace'te kullanıcıya yansıyan termination reason dağılımı
curl -s http://localhost:5021/traces/stats | jq .terminationReasons

# Bir session'ın tüm trace'leri
curl -s http://localhost:5021/traces/by-session/<SID> | jq '.[] | {traceId, userQuery, terminationReason}'

# Evaluation senaryolarını koş, sadece fail'leri göster
curl -sX POST "http://localhost:5021/eval/run" \
  | jq '.results[] | select(.passed == false)'
```

---

Sorularınız olursa: [architecture.md](architecture.md), [domain/Model-Reasoning.md](domain/Model-Reasoning.md), [agentic-patterns.md](agentic-patterns.md) dokümanlarını ilk uğrak olarak öneririz.
