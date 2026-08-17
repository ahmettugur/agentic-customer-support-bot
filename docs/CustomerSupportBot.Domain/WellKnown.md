# WellKnown — Magic String Registry

**Dosya:** `Model/WellKnown.cs`

Projedeki **tüm magic string** değerlerinin tek doğruluk kaynağı. Intent name, agent name, tool name, status, fallback message, eşik değerleri — hepsi burada.

---

## Neden var?

Magic string'ler kodun her yerine dağılırsa:
- ❌ Typo'lar runtime'da hataya dönüşür (`"OrderInquiy"` derleyici hatası vermez)
- ❌ Bir değer değiştiğinde tüm proje aranır
- ❌ Lokalizasyon zor

`WellKnown` ile:
- ✅ Tek nokta — değişiklik bir yerde
- ✅ IntelliSense desteği
- ✅ Refactor-safe

---

## İçerik kategorileri

### Intents

```csharp
public static class Intents
{
    public const string Unknown = "bilinmiyor";           // sadece parser fallback'i, LLM üretmez
    public const string OrderCreation = "sipariş_oluşturma";
    public const string OrderInquiry = "sipariş_sorgulama";
    public const string OrderListing = "sipariş_listeleme";
    public const string OrderCancellation = "sipariş_iptali";
    public const string ReturnRequest = "iade_talebi";
    public const string Complaint = "şikayet";
    public const string ProductInfo = "ürün_bilgisi";
    public const string HumanHandoffRequest = "talep_temsilci";
    public const string General = "genel";

    public static readonly IReadOnlySet<string> LlmProduced;   // yukarıdakilerin tümü, Unknown hariç
}
```

**Bu sınıf, intent kelime dağarcığının TEK doğruluk kaynağıdır.** Reasoning LLM'i `Prompts/services/reasoning-system.md` içindeki bir enum satırından hangi intent değerlerini üretebileceğini öğrenir; `ReasoningResultParser` bu string'i **hiçbir normalizasyon yapmadan** olduğu gibi taşır. Yani prompt'taki enum ile buradaki sabitler kelimesi kelimesine aynı olmak zorunda — aksi halde derleme hatası **vermeden** sessizce kırılır.

Bu tam olarak başımıza geldi: prompt bir ara `"sipariş_iadesi"` yazıyordu, kod ise `ReturnRequest = "iade_talebi"` tanımlıyordu. `appsettings.json`'daki `IntentSkillMap["iade_talebi"]` LLM'in ürettiği `"sipariş_iadesi"` ile hiç eşleşmedi — sonuç: iade eskalasyonlarına hiçbir zaman `refund` yetkinliği (skill) gerekmedi, ve durum hiçbir yerde hata olarak görünmedi (derleyici sessiz, testler farkında değil). Bunu yakalayan tek şey `PromptContractTests.ReasoningPrompt_IntentEnum_MatchesWellKnownIntents` testiydi.

`LlmProduced`, prompt ↔ kod senkronunu **test edilebilir** kılmak için eklendi: `IntentSkillMap` gibi tam-string eşleşme yapan tüketiciler artık bu kümeye göre doğrulanabiliyor. Yeni bir intent eklerken sırasıyla: (1) burada sabit ekle, (2) `reasoning-system.md`'deki enum satırına ekle, (3) `LlmProduced`'a ekle, (4) gerekiyorsa `appsettings.json`'daki `IntentSkillMap`'e satır ekle.

### Confidence (Türkçe + English varyantları)

```csharp
public static class Confidence
{
    public const string High = "yüksek";
    public const string Medium = "orta";
    public const string Low = "düşük";

    public const string HighEn = "high";
    public const string MediumEn = "medium";
    public const string LowEn = "low";
}
```

LLM hem "yüksek" hem "high" üretebilir; parser ikisini de tanır.

### Phases

```csharp
public static class Phases
{
    public const string Greeting = "greeting";
    public const string Inquiry = "inquiry";
    public const string Action = "action";
    public const string Resolution = "resolution";
}
```

### AgentNames

```csharp
public static class AgentNames
{
    public const string Planning = "PlanningAgent";
    public const string Product = "ProductAgent";
    public const string Order = "OrderAgent";
    public const string Complaint = "ComplaintAgent";
    public const string HumanHandoff = "HumanHandoffAgent";
    public const string Response = "ResponseAgent";

    public static readonly IReadOnlySet<string> ReadOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Product  // approval gerektirmez
    };

    public static readonly string[] Specialists =
    [
        Product, Order, Complaint, HumanHandoff
    ];

    public static readonly string[] All =
    [
        Planning, Product, Order, Complaint, HumanHandoff, Response
    ];
}
```

### ToolNames

```csharp
public static class ToolNames
{
    public const string ProductInquiry = "product_inquiry_tool";
    public const string ProductList = "product_list_tool";
    public const string OrderPlacement = "order_placement_tool";
    public const string OrderStatus = "order_status_tool";
    public const string GetLastOrder = "get_last_order_tool";
    public const string GetAllOrders = "get_all_orders_tool";
    public const string OrderCancel = "order_cancel_tool";
    public const string ReturnRequest = "return_request_tool";
    public const string ComplaintRegistration = "complaint_registration_tool";
    public const string HumanHandoff = "human_handoff_tool";
}

public static readonly IReadOnlySet<string> HighRiskTools = new HashSet<string>
{
    ToolNames.OrderPlacement,
    ToolNames.OrderCancel,
    ToolNames.ReturnRequest,
    ToolNames.ComplaintRegistration
};
```

**HighRiskTools:** Admin approve etmek için gerekçe (reason) zorunludur — audit trail için. `AdminEndpoints` bu listeyi approve request'te gerekçe validasyonu için kullanır.

### TaskStatuses

```csharp
public static class TaskStatuses
{
    public const string Done = "done";
    public const string NeedsFollowUp = "needs_followup";
    public const string NeedsEscalation = "needs_escalation";
    public const string Failed = "failed";
    public const string Partial = "partial";

    // NormalizeStatus tarafından kabul edilen alias'lar
    public const string Completed = "completed";
    public const string Complete = "complete";
    public const string Success = "success";
    public const string Followup = "followup";
    public const string NeedsFollowupNoUnderscore = "needsfollowup";
    public const string Escalation = "escalation";
    public const string Escalate = "escalate";
    public const string Error = "error";
    public const string Fail = "fail";
    public const string Incomplete = "incomplete";
}
```

### Termination

```csharp
public static class Termination
{
    public const string Marker = "TERMINATE";

    // LLM (ResponseAgent) tarafından üretilen reason'lar
    // (response-agent.md prompt'undaki listeyle birebir hizalı)
    public const string ReasonCompleted = "completed";
    public const string ReasonAwaitingUserInput = "awaiting_user_input";
    public const string ReasonEscalationNeeded = "escalation_needed";
    public const string ReasonNotFound = "not_found";
    public const string ReasonError = "error";

    // Sistem (guard) tarafından üretilen reason'lar — LLM bunları üretmez
    public const string ReasonMaxMessages = "max_messages_reached";
    public const string ReasonRepeatedToolCall = "repeated_tool_call_guard";
    public const string ReasonTimeout = "timeout";

    // Bilinen tüm reason değerleri (case-insensitive) —
    // WorkflowResponseExtractor.ParseTerminationReasonFromResult bu kümeyle doğrular
    public static readonly IReadOnlySet<string> KnownReasons;
}
```

LLM yanıtın sonuna `TERMINATE` yazınca workflow sonlanır. Bilinmeyen bir reason parse edilirse warning log'lanır ve `completed` fallback'i uygulanır.

### EscalationActions

```csharp
public static class EscalationActions
{
    public const string Acknowledge = "acknowledge";
    public const string Ack = "ack";
    public const string Resolve = "resolve";
    public const string Dismiss = "dismiss";
}
```

### OrderStatuses (Türkçe)

```csharp
public static class OrderStatuses
{
    public const string Processing = "İşleniyor";
    public const string Shipped = "Kargolandı";
    public const string Delivered = "Teslim Edildi";
    public const string Cancelled = "İptal Edildi";
    public const string ReturnRequested = "İade Talep Edildi";  // YENİ
    public const string ReturnApproved = "İade Onaylandı";      // YENİ
}
```

### ComplaintStatuses

```csharp
public static class ComplaintStatuses
{
    public const string Pending = "Beklemede";
    public const string InProgress = "İncelemede";
    public const string Resolved = "Çözüldü";
}
```

### FallbackMessages (Türkçe LLM hata fallback)

```csharp
public static class FallbackMessages
{
    public const string ReasoningUnavailable = "Reasoning şu anda kullanılamıyor.";
    public const string ReasoningIncomplete = "Reasoning tamamlanamadı.";
    public const string AnalysisParseFailed = "(Analiz üretilemedi; JSON çıktısı bozuk)";
    public const string RoutingRewrite = "Talebinizi inceliyorum. Lütfen müşteri kimlik numaranızı paylaşır mısınız?";
    public const string ApprovalRejected = "İşlem onaylanmadı";
    public const string ComplaintRejected = "Şikayet kaydı onaylanmadı";
    public const string RequestCancelled = "İstek iptal edildi (timeout veya bağlantı koptu)";
    public const string NewChat = "Yeni sohbet";
    public const string HumanJoined = "Müşteri temsilcisi {0} sohbete katıldı.";
    public const string HumanLeft = "Müşteri temsilcisi sohbeti sonlandırdı. Bot moduna dönüldü.";
    public const string LiveTakeoverResolution = "Canlı sohbet üzerinden çözüldü (Live Takeover).";
    public const string ReplanResolution = "Admin sohbeti yeniden planlattı; bot kontrolünde devam ediyor.";
    public const string ReplanCustomerNotice = "ℹ️ Talebinizi tekrar değerlendiriyoruz.";
    public const string ReplanPlanningHint = "🔄 ADMIN OVERRİDE — ...";  // PlanningAgent'a iletilen hint
}
```

LLM 500/timeout aldığında bu mesajlar kullanılır — kullanıcı boş yanıt almaz. `HumanJoined` ve `HumanLeft` runtime'da `string.Format` ile doldurulur.

### ResponseKeywords

```csharp
public static class ResponseKeywords
{
    public const string SuccessMarker = "başarıyla";   // Phase=Resolution tetikler
    public const string MissingInfoMarker = "EKSİK_BİLGİ";   // Phase=Inquiry tetikler
}
```

`SessionStateExtractor` bot mesajında bu marker'ları arar.

### ToolErrorCodes

```csharp
public static class ToolErrorCodes
{
    public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string StockInsufficient = "STOCK_INSUFFICIENT";
    public const string CustomerIdMismatch = "CUSTOMER_ID_MISMATCH";
    public const string NoOrdersForCustomer = "NO_ORDERS_FOR_CUSTOMER";
    public const string OrderAlreadyCancelled = "ORDER_ALREADY_CANCELLED";
    public const string OrderNotCancellable = "ORDER_NOT_CANCELLABLE";
    public const string ReturnNotEligible = "RETURN_NOT_ELIGIBLE";
    public const string ReturnAlreadyRequested = "RETURN_ALREADY_REQUESTED";
}
```

> `CATEGORY_NOT_FOUND` ile `PRODUCT_NOT_FOUND` **kasıtlı olarak ayrıdır**: birincisinde
> kategori adı yanlıştır ve geçerli bir adla tekrar denemek doğrudur, ikincisinde kategori
> vardır ama içi boştur ve tekrar denemek anlamsızdır. İkisi eskiden tek koda düşüyor ve LLM
> hangi durumda olduğunu bilemiyordu — bkz.
> [`CategoryProducts.md`](Model/CategoryProducts.md).

### ReasoningEffort

```csharp
public static class ReasoningEffort
{
    public const string PropertyKey = "reasoning_effort";
}
```

O-series modeller için reasoning effort seviyesi anahtar adı.

### ChatModes

```csharp
public static class ChatModes
{
    public const string Human = "human";
    public const string Bot = "bot";
}
```

JSON serialization için chat mod etiketleri.

### JsonProperties

```csharp
public static class JsonProperties
{
    public const string PreToolCheck = "preToolCheck";
    public const string ResultConfidence = "resultConfidence";
    public const string PostToolReflection = "postToolReflection";
    public const string SelfCritique = "selfCritique";
    public const string SelectedAgent = "selectedAgent";
    public const string Steps = "steps";
    public const string Analysis = "analysis";
    public const string Intent = "intent";
}
```

LLM çıktı parse işlemleri için JSON property adları.

### SystemExecutorPrefixes

```csharp
public static class SystemExecutorPrefixes
{
    public static readonly string[] Values =
    [
        "GroupChatHost"
    ];
}
```

MAF sistem executor önekleri — kullanıcıya ajan gibi gösterilmez; `WorkflowResponseExtractor.IsInternalWorkflowExecutor` bunları filtreler.

> 🐞 **Ölçülerek daraltıldı — 4 girdi hiçbir şeyle eşleşmiyordu.** Liste eskiden
> `GroupChatManager`, `RoundRobinGroupChatManager`, `StartExecutor`, `EndExecutor` de
> içeriyordu. MAF 1.17.0 ile gerçek production workflow'u (`AgentTeamFactory.CreateWorkflow`)
> kurulup `ReflectEdges()` ile ölçüldüğünde üretilen id'ler yalnızca şunlar:
>
> ```
> GroupChatHost                                    ← tek sistem düğümü
> PlanningAgent_4a58d12814a44688afbe770cc1830ea6   ← {AjanAdı}_{guid}
> ProductAgent_28567321710f4b80a7a154caadd4566b
> OrderAgent_1d8abdff1bc343999fee3e9e26baac31
> ComplaintAgent_2f32cfc1e1a3424daebcc1f7e7bea010
> HumanHandoffAgent_f4cba86a08cc42fb93c7d008016dba0e
> ResponseAgent_2a22ab9b3b9a420bb2c5b8c0a88d0483
> ```
>
> Silinen 4 girdi MAF'ın **hiçbir** topolojisinde executor id olarak görünmüyor; filtreyi
> genişletmiş gibi görünüp aslında hiçbir şey korumuyor, korumanın gerçekte **tek bir dizeye**
> (`GroupChatHost`) dayandığını gizliyorlardı. `IsInternalWorkflowExecutor_NonExistentMafIds_NotFiltered`
> testi geri eklenmelerini engeller.

> ⚠️ **Topoloji değişirse burası güncellenmeli.** Ölçülen diğer topolojilerin sistem düğümleri
> farklı adlar taşır ve bu liste onları **yakalamaz**: Concurrent → `Start`, `Batcher/{AjanAdı}_{guid}`;
> Handoff → `HandoffStart`; Sequential → (sistem düğümü yok). Bugün yalnızca GroupChat kullanılıyor.

### ApprovalReasons

```csharp
public static class ApprovalReasons
{
    public const string AutoApproveTimeout = "Timeout — otomatik onaylandı";
    public const string TimeoutExpired = "Onay süresi doldu (admin karar vermedi)";
    public const string AdminRejected = "admin reddetti";
    public const string AgentWantsToCall = "{0} bu tool'u çağırmak istiyor.";
}
```

### Evaluation

```csharp
public static class Evaluation
{
    public const string ScenarioFileName = "evaluation-scenarios.yaml";
}
```

### ToolParameterNames

```csharp
public static class ToolParameterNames
{
    public const string CustomerId = "customer_id";
    public const string OrderId = "order_id";
    public const string ProductName = "product_name";
    public const string Quantity = "quantity";
    public const string Lines = "lines";
    public const string Reason = "reason";
    public const string ComplaintDescription = "description";
}
```

Tool parametrelerinin standart isimleri — `CollectedInfo` Dict'iyle uyumlu.

> `Lines`, `order_placement_tool`'un satır listesi parametresidir (çok ürünlü sipariş). Satırların tamamı eksikse eksik-alan bildiriminde `ProductName`/`Quantity` yerine bu ad kullanılır — LLM'in doldurması gereken alan tek başına ürün adı değil, satırların tamamıdır. Bkz. [OrderToolsService](../CustomerSupportBot.Application/Tools/OrderToolsService.md).

### Defaults

```csharp
public static class Defaults
{
    public const string Admin = "admin";
    public const string System = "system";
}
```

Genel amaçlı sabit değerler — ör. `decidedBy`/`actor` alanlarında varsayılan kimlik.

---

## Keyword tabloları (rule-based detection)

### IntentKeywords

```csharp
public static readonly IReadOnlyList<(string Intent, string[] Keywords)> IntentKeywords =
[
    (Intents.OrderCreation,     ["sipariş ver", "almak istiyorum", "sipariş etmek"]),
    (Intents.OrderCancellation, ["iptal", "siparişimi iptal", "iptal et", "vazgeçtim"]),
    (Intents.ReturnRequest,     ["iade", "iade etmek", "geri göndermek", "iade talebi", "ürünü iade"]),
    (Intents.OrderListing,      ["son sipariş", "tüm sipariş", "siparişlerim"]),
    (Intents.Complaint,         ["şikayet", "memnun değil", "sorun"]),
    (Intents.ProductInfo,       ["ürün", "fiyat", "stok"]),
];
```

Sıra önemlidir — ilk eşleşen intent seçilir. `SessionStateExtractor` bu tabloyu kullanır.

### SentimentKeywords

```csharp
public static readonly IReadOnlyList<(string Sentiment, double Score, string[] Keywords)> SentimentKeywords =
[
    (Sentiments.Angry,    0.1,  ["rezalet", "skandal", "saçmalık", "berbat", "iğrenç", ...]),
    (Sentiments.Negative, 0.25, ["memnun değil", "kötü", "sorun", "problem", "hata", ...]),
    (Sentiments.Positive, 0.85, ["teşekkür", "sağol", "harika", "mükemmel", "süper", ...]),
];
```

Sıra: angry → negative → positive, **ilk eşleşen kazanır**. Eşleşme yoksa `neutral` (0.5). `SessionStateExtractor.DetectSentiment` bu tabloyu, yalnızca LLM sinyal üretmediğinde fallback olarak kullanır (bkz. `TurnSignals` ve `Services-SessionStateExtractor.md`).

**Önemli:** Negative listesine "işlem adı" olan kelimeler (`iade`, `iptal`, `şikayet`) girmez. Bunlar kullanıcının yapmak istediği işi tarif eder, duygusunu değil — listedeyken *"teşekkürler, iade işlemim tamamlandı"* gibi memnun mesajlar negatif sayılıp `ConsecutiveNegativeTurns` sayacını (ve dolayısıyla otomatik eskalasyon eşiğini) yanlış yere çekiyordu. Gerçekten öfkeli varyantları (`"şikayet edeceğim"`) zaten Angry listesinde. Aynı gerekçeyle Positive listesindeki `"çözüldü"`nün öneki olan `"çöz"` de Negative'den çıkarıldı.

Sıra da load-bearing: bir kelime bir sonraki listedeki kelimenin **öneki** olamaz — `"memnun değil"` (negative) `"memnun"`dan (positive) önce kontrol edilmezse yanlış sonuç çıkar. Bilinen bir sınır kaldı: eşleşme saf substring olduğu için `"sorun"` kelimesi *"sorunum çözüldü"* gibi olumlu bir cümlede de yakalanır (kelime sınırı/olumsuzlama analizi bu tablonun kapsamında değil). Etkisi sınırlı, çünkü gerçek boru hattında LLM'in sentiment'i önceliklidir — bu tablo yalnızca LLM sinyal üretmediğinde devreye girer.

### SentimentThresholds

```csharp
public static class SentimentThresholds
{
    public const double AngryThreshold = 0.15;
    public const double NegativeThreshold = 0.35;
    public const double PositiveThreshold = 0.65;
    public const int AutoEscalationConsecutiveNegative = 3;
}
```

Threshold'lar burada **tek yerde** — değiştirilmek istenirse hiçbir adapter düzenlenmez.

---

## Kullanım örneği

```csharp
// Magic string yerine:
if (status == "Kargoda") { ... }

// Doğru kullanım:
if (status == WellKnown.OrderStatuses.Shipped) { ... }


// LLM çıktısı normalize:
var normalizedStatus = result.Status switch
{
    WellKnown.TaskStatuses.Done => TaskCompletionStatus.Done,
    "completed" => TaskCompletionStatus.Done,   // alias
    // ...
};
```

---

## Bağımlılıklar

`WellKnown` Domain'in **hiçbir** sınıfına bağımlı değildir — sadece string sabit ve readonly collection içerir. Bu sayede her domain modeli rahatlıkla import edebilir.

`Adapters` ve `Application` katmanları da bunu kullanır:

```
Application/SkillsBasedRouter      → WellKnown.Intents
Adapters.Agents/PlanningAgent      → WellKnown.AgentNames
Adapters.Agents/Tools/OrderTool    → WellKnown.ToolErrorCodes
Adapters.Persistence/InMemoryOrder → WellKnown.OrderStatuses
```
