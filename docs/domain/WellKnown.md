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
    public const string Unknown = "Unknown";
    public const string OrderCreation = "OrderCreation";
    public const string OrderInquiry = "OrderInquiry";
    public const string OrderListing = "OrderListing";
    public const string Complaint = "Complaint";
    public const string ProductInfo = "ProductInfo";
    public const string General = "General";
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
    public const string Greeting = "Greeting";
    public const string Inquiry = "Inquiry";
    public const string Action = "Action";
    public const string Resolution = "Resolution";
}
```

### AgentNames

```csharp
public static class AgentNames
{
    public const string Planning = "PlanningAgent";
    public const string ProductInquiry = "ProductInquiryAgent";
    public const string Order = "OrderAgent";
    public const string Complaint = "ComplaintAgent";
    public const string HumanHandoff = "HumanHandoffAgent";
    public const string Response = "ResponseAgent";

    public static readonly HashSet<string> ReadOnly = new()
    {
        ProductInquiry  // approval gerektirmez
    };

    public static readonly string[] Specialists =
    {
        ProductInquiry, Order, Complaint, HumanHandoff
    };
}
```

### ToolNames

```csharp
public static class ToolNames
{
    public const string ProductInquiry = "product_inquiry_tool";
    public const string OrderPlacement = "order_placement_tool";
    public const string OrderStatus = "order_status_tool";
    public const string OrdersByCustomer = "orders_by_customer_tool";
    public const string GetLastOrder = "get_last_order_tool";
    public const string ComplaintRegistration = "complaint_registration_tool";
    public const string HumanHandoff = "human_handoff_tool";
}

public static readonly HashSet<string> HighRiskTools = new()
{
    ToolNames.OrderPlacement,
    ToolNames.ComplaintRegistration
};
```

**HighRiskTools:** Approval + justification zorunlu. `WorkflowExecutor` bunları çağıramaz (`ForbiddenTools`).

### TaskStatuses

```csharp
public static class TaskStatuses
{
    public const string Done = "done";
    public const string NeedsFollowUp = "needs_followup";
    public const string NeedsEscalation = "needs_escalation";
    public const string Failed = "failed";
    public const string Partial = "partial";
}
```

### Termination

```csharp
public static class Termination
{
    public const string Marker = "TERMINATE";
    public const string ReasonCompleted = "completed";
    public const string ReasonMaxMessages = "max_messages";
    public const string ReasonTimeout = "timeout";
    public const string ReasonError = "error";
    public const string ReasonTerminatedByRestart = "terminated_by_restart";
}
```

LLM yanıtın sonuna `TERMINATE` yazınca workflow sonlanır.

### EscalationActions

```csharp
public static class EscalationActions
{
    public const string Acknowledge = "Acknowledge";
    public const string Resolve = "Resolve";
    public const string Dismiss = "Dismiss";
}
```

### OrderStatuses (Türkçe)

```csharp
public static class OrderStatuses
{
    public const string Processing = "Hazırlanıyor";
    public const string Shipped = "Kargoda";
    public const string Delivered = "Teslim Edildi";
    public const string Cancelled = "İptal Edildi";
}
```

### ComplaintStatuses

```csharp
public static class ComplaintStatuses
{
    public const string Pending = "Beklemede";
    public const string InProgress = "İnceleniyor";
    public const string Resolved = "Çözüldü";
}
```

### FallbackMessages (Türkçe LLM hata fallback)

```csharp
public static class FallbackMessages
{
    public const string ReasoningUnavailable = "Şu anda analiz yapamıyorum, lütfen tekrar deneyin.";
    public const string RoutingRewrite = "Sorunuzu daha iyi anlayabilmek için biraz daha ayrıntı verir misiniz?";
    public const string ApprovalRejected = "Bu işlem için onay alınamadı.";
    public const string SystemError = "Sistem geçici bir hata yaşıyor.";
    // ...
}
```

LLM 500/timeout aldığında bu mesajlar kullanılır — kullanıcı boş yanıt almaz.

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
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string StockInsufficient = "STOCK_INSUFFICIENT";
    public const string ComplaintDuplicate = "COMPLAINT_DUPLICATE";
    public const string OperationNotAllowed = "OPERATION_NOT_ALLOWED";
    public const string SystemUnavailable = "SYSTEM_UNAVAILABLE";
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
    (Intents.OrderCreation, new[] { "sipariş ver", "satın al", "ürün al" }),
    (Intents.OrderInquiry,  new[] { "sipariş durumu", "kargoda mı", "nerede" }),
    (Intents.Complaint,     new[] { "şikayet", "memnun değil", "iade", "ürün bozuk" }),
    (Intents.ProductInfo,   new[] { "ürün bilgisi", "fiyat", "stok" }),
    // ...
];
```

`SessionStateExtractor` ve `WorkflowExecutor` bu tabloyu kullanır.

### SentimentKeywords

```csharp
public static readonly IReadOnlyList<(string Label, double Score, string[] Keywords)> SentimentKeywords =
[
    ("angry",    0.10, new[] { "berbat", "rezalet", "çileden çıkardın" }),
    ("negative", 0.25, new[] { "kötü", "memnun değil", "yetersiz" }),
    ("positive", 0.85, new[] { "harika", "teşekkür", "süper", "mükemmel" }),
];
```

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
