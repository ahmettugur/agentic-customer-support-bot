// Models/WellKnown.cs
// Uygulama genelinde kullanılan magic string'lerin merkezi sabit sınıfı.
// Her alt sınıf farklı bir domain kavramını gruplar.

namespace CustomerSupportBot.Domain.Model;

/// <summary>Tüm magic string sabitlerinin tek adresi.</summary>
public static class WellKnown
{
    /// <summary>Algılanan kullanıcı niyetleri (intent).</summary>
    public static class Intents
    {
        public const string Unknown = "bilinmiyor";
        public const string OrderCreation = "sipariş_oluşturma";
        public const string OrderInquiry = "sipariş_sorgulama";
        public const string OrderListing = "sipariş_listeleme";
        public const string Complaint = "şikayet";
        public const string ProductInfo = "ürün_bilgisi";
        public const string General = "genel";
    }

    /// <summary>Güven seviyesi etiketleri (legacy string).</summary>
    public static class Confidence
    {
        public const string High = "yüksek";
        public const string Medium = "orta";
        public const string Low = "düşük";

        // İngilizce karşılıklar (LLM çıktısı uyumu için)
        public const string HighEn = "high";
        public const string MediumEn = "medium";
        public const string LowEn = "low";
    }

    /// <summary>Konuşma fazları.</summary>
    public static class Phases
    {
        public const string Greeting = "greeting";
        public const string Inquiry = "inquiry";
        public const string Action = "action";
        public const string Resolution = "resolution";
    }

    /// <summary>Ajan isimleri — MAF executor ID'leri ve prompt referansları.</summary>
    public static class AgentNames
    {
        public const string Planning = "PlanningAgent";
        public const string ProductInquiry = "ProductInquiryAgent";
        public const string Order = "OrderAgent";
        public const string Complaint = "ComplaintAgent";
        public const string HumanHandoff = "HumanHandoffAgent";
        public const string Response = "ResponseAgent";

        /// <summary>Yan-etkisiz (read-only) specialist ajanlar — paralel sub-task çalıştırması için.</summary>
        public static readonly IReadOnlySet<string> ReadOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ProductInquiry
        };

        /// <summary>Specialist ajanlar (Planning ve Response hariç).</summary>
        public static readonly string[] Specialists =
        [
            ProductInquiry, Order, Complaint, HumanHandoff
        ];

        /// <summary>Tüm ajanlar.</summary>
        public static readonly string[] All =
        [
            Planning, ProductInquiry, Order, Complaint, HumanHandoff, Response
        ];
    }

    /// <summary>Tool isimleri — AIFunctionFactory name parametresi ve HITL config.</summary>
    public static class ToolNames
    {
        public const string ProductInquiry = "product_inquiry_tool";
        public const string OrderPlacement = "order_placement_tool";
        public const string OrderStatus = "order_status_tool";
        public const string ComplaintRegistration = "complaint_registration_tool";
        public const string GetLastOrder = "get_last_order_tool";
        public const string GetAllOrders = "get_all_orders_tool";
        public const string HumanHandoff = "human_handoff_tool";
    }

    /// <summary>
    /// Yüksek riskli yan etkili tool'lar — admin onayında gerekçe (audit trail) zorunludur.
    /// Bu liste frontend (admin.js HIGH_RISK_TOOLS) ile senkron tutulmalı.
    /// </summary>
    public static readonly IReadOnlySet<string> HighRiskTools = new HashSet<string>
    {
        ToolNames.OrderPlacement,
        ToolNames.ComplaintRegistration
    };

    /// <summary>Görev tamamlanma durumları — PostToolReflection.Status ve NormalizeStatus.</summary>
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

    /// <summary>Workflow sonlandırma işaretçileri ve nedenleri.</summary>
    public static class Termination
    {
        public const string Marker = "TERMINATE";
        public const string ReasonCompleted = "completed";
        public const string ReasonMaxMessages = "max_messages_reached";
        public const string ReasonRepeatedToolCall = "repeated_tool_call_guard";
        public const string ReasonTimeout = "timeout";
    }

    /// <summary>Eskalasyon aksiyonları — InMemoryEscalationSink.Decide.</summary>
    public static class EscalationActions
    {
        public const string Acknowledge = "acknowledge";
        public const string Ack = "ack";
        public const string Resolve = "resolve";
        public const string Dismiss = "dismiss";
    }

    /// <summary>Varsayılan isimler ve roller.</summary>
    public static class Defaults
    {
        public const string Admin = "admin";
        public const string System = "system";
    }

    /// <summary>Türkçe fallback mesajlar — LLM çağrısı başarısız olduğunda kullanılır.</summary>
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
        public const string ReplanCustomerNotice = "ℹ️ Talebinizi tekrar değerlendiriyoruz. Lütfen ne ile ilgili yardım istediğinizi kısaca yazar mısınız?";
        public const string ReplanPlanningHint = "🔄 ADMIN OVERRİDE — Önceki TOOL ÇAĞRILARINI ve specialist agent kararlarını geçersiz say. Ancak müşteri ile temsilci arasında geçen son yazışmalardaki bağlamı (verilen sözler, yönlendirmeler, paylaşılan ID'ler/numaralar, temsilcinin seçtiği rota) AYNEN KORU ve dikkate al. Müşterinin BU TURDAKİ mesajını bu bağlam ışığında sıfırdan analiz et ve doğru specialist'i seç. Önceki yanlış rotalama kalıbını tekrar etme.";
    }

    /// <summary>Bot yanıtında fazı tespit etmek için kullanılan anahtar kelimeler.</summary>
    public static class ResponseKeywords
    {
        public const string SuccessMarker = "başarıyla";
        public const string MissingInfoMarker = "EKSİK_BİLGİ";
    }

    /// <summary>Reasoning effort seviyesi — o-series modeller için.</summary>
    public static class ReasoningEffort
    {
        public const string PropertyKey = "reasoning_effort";
    }

    /// <summary>Chat mode etiketleri — JSON serialization.</summary>
    public static class ChatModes
    {
        public const string Human = "human";
        public const string Bot = "bot";
    }

    /// <summary>JSON property adları — LLM çıktı parse işlemleri için.</summary>
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

    /// <summary>MAF sistem executor önekleri — kullanıcıya gösterilmez.</summary>
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

    /// <summary>Onay gerekçesi mesajları.</summary>
    public static class ApprovalReasons
    {
        public const string AutoApproveTimeout = "Timeout — otomatik onaylandı (config: AutoApproveOnTimeout=true)";
        public const string TimeoutExpired = "Onay süresi doldu (admin karar vermedi)";
        public const string AdminRejected = "admin reddetti";
        public const string AgentWantsToCall = "{0} bu tool'u çağırmak istiyor.";
    }

    /// <summary>Değerlendirme (evaluation) dosya adı.</summary>
    public static class Evaluation
    {
        public const string ScenarioFileName = "evaluation-scenarios.yaml";
    }

    /// <summary>Sipariş durum etiketleri (Türkçe). UI ve veri modelinde kullanılır.</summary>
    public static class OrderStatuses
    {
        public const string Processing = "İşleniyor";
        public const string Shipped = "Kargolandı";
        public const string Delivered = "Teslim Edildi";
        public const string Cancelled = "İptal Edildi";
    }

    /// <summary>Şikayet durum etiketleri (Türkçe).</summary>
    public static class ComplaintStatuses
    {
        public const string Pending = "Beklemede";
        public const string InProgress = "İncelemede";
        public const string Resolved = "Çözüldü";
    }

    /// <summary>Tool error code'ları (makine-okunur).</summary>
    public static class ToolErrorCodes
    {
        public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
        public const string ProductNotFound = "PRODUCT_NOT_FOUND";
        public const string OrderNotFound = "ORDER_NOT_FOUND";
        public const string StockInsufficient = "STOCK_INSUFFICIENT";
        public const string CustomerIdMismatch = "CUSTOMER_ID_MISMATCH";
        public const string NoOrdersForCustomer = "NO_ORDERS_FOR_CUSTOMER";
    }

    /// <summary>Tool parametre adları (snake_case) — validation MissingFields için.</summary>
    public static class ToolParameterNames
    {
        public const string CustomerId = "customer_id";
        public const string OrderId = "order_id";
        public const string ProductName = "product_name";
        public const string Quantity = "quantity";
        public const string Reason = "reason";
        public const string ComplaintDescription = "description";
    }

    /// <summary>OpenTelemetry ActivitySource ve Meter isimleri — tek kaynak noktası.</summary>
    public static class Telemetry
    {
        public const string ActivitySourceName = "CustomerSupportBot.Api";
        public const string MeterName = "CustomerSupportBot.Api";
    }

    /// <summary>
    /// Niyet algılama anahtar kelimeleri.
    /// Sıraya dikkat: ilk eşleşen niyet seçilir.
    /// </summary>
    public static readonly IReadOnlyList<(string Intent, string[] Keywords)> IntentKeywords =
    [
        (Intents.OrderCreation, ["sipariş ver", "almak istiyorum", "sipariş etmek"]),
        (Intents.OrderListing, ["son sipariş", "tüm sipariş", "siparişlerim"]),
        (Intents.Complaint, ["şikayet", "memnun değil", "sorun"]),
        (Intents.ProductInfo, ["ürün", "fiyat", "stok"]),
    ];

    // ═══════════════════════════════════════════════
    // DUYGU ANALİZİ (Sentiment Analysis)
    // ═══════════════════════════════════════════════

    /// <summary>Duygu etiketleri — LLM ve kural tabanlı analiz çıktısı.</summary>
    public static class Sentiments
    {
        public const string Positive = "positive";
        public const string Neutral = "neutral";
        public const string Negative = "negative";
        public const string Angry = "angry";
    }

    /// <summary>Duygu eşikleri.</summary>
    public static class SentimentThresholds
    {
        /// <summary>Bu skorun altında negative kabul edilir.</summary>
        public const double NegativeThreshold = 0.35;

        /// <summary>Bu skorun altında angry/çok olumsuz kabul edilir.</summary>
        public const double AngryThreshold = 0.15;

        /// <summary>Bu skorun üstünde positive kabul edilir.</summary>
        public const double PositiveThreshold = 0.65;

        /// <summary>Ardışık negatif tur sayısı bu değere ulaşırsa otomatik eskalasyon tetiklenir.</summary>
        public const int AutoEscalationConsecutiveNegative = 3;
    }

    /// <summary>
    /// Kural tabanlı duygu analizi anahtar kelimeleri.
    /// Sıra: angry → negative → positive. Eşleşme yoksa neutral.
    /// </summary>
    public static readonly IReadOnlyList<(string Sentiment, double Score, string[] Keywords)> SentimentKeywords =
    [
        // Angry (çok olumsuz) — 0.1
        (Sentiments.Angry, 0.1, [
            "rezalet", "skandal", "saçmalık", "berbat", "iğrenç", "korkunç",
            "felaket", "kabul edilemez", "utanç verici", "terbiyesiz",
            "dava açacağım", "avukat", "şikayet edeceğim", "tüketici hakları"
        ]),
        // Negative (olumsuz) — 0.25
        (Sentiments.Negative, 0.25, [
            "memnun değil", "kötü", "sorun", "problem", "hata", "yanlış",
            "gecikmeli", "gecikme", "eksik", "kırık", "bozuk", "hasar",
            "iade", "iptal", "şikayet", "düzelt", "çöz", "mutsuz",
            "sinir", "kızgın", "üzgün", "hayal kırıklığı", "beklentim",
            "olmadı", "çalışmıyor", "gelmedi", "kayıp", "neden böyle"
        ]),
        // Positive (olumlu) — 0.85
        (Sentiments.Positive, 0.85, [
            "teşekkür", "sağol", "harika", "mükemmel", "süper", "güzel",
            "memnun", "mutlu", "başarılı", "iyi", "bravo", "tebrik",
            "çok iyi", "hızlı", "yardımcı", "ilgilendi", "çözüldü",
            "tatmin", "sevindim", "beğendim"
        ])
    ];
}

