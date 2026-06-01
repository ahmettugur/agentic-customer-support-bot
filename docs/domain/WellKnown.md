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
    public const string Unknown = "bilinmiyor";
    public const string OrderCreation = "sipariş_oluşturma";
    public const string OrderInquiry = "sipariş_sorgulama";
    public const string OrderListing = "sipariş_listeleme";
    public const string OrderCancellation = "sipariş_iptali";  // YENİ
    public const string ReturnRequest = "iade_talebi";           // YENİ
    public const string Complaint = "şikayet";
    public const string ProductInfo = "ürün_bilgisi";
    public const string General = "genel";
}
```

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
    public const string ReasonCompleted = "completed";
    public const string ReasonMaxMessages = "max_messages_reached";
    public const string ReasonRepeatedToolCall = "repeated_tool_call_guard";
    public const string ReasonTimeout = "timeout";
}
```

LLM yanıtın sonuna `TERMINATE` yazınca workflow sonlanır.

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
        "GroupChatHost",
        "GroupChatManager",
        "RoundRobinGroupChatManager",
        "StartExecutor",
        "EndExecutor"
    ];
}
```

MAF sistem executor önekleri — kullanıcıya gösterilmez; `WorkflowResponseExtractor` bunları filtreler.

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
    public const string Reason = "reason";
    public const string Description = "description";
}
```

Tool parametrelerinin standart isimleri — `CollectedInfo` Dict'iyle uyumlu.

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

Sıra: angry → negative → positive. Eşleşme yoksa `neutral`.

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
