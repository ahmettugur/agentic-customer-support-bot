// Application/Services/Budget/LlmBudgetOptions.cs
// appsettings "LlmBudget": günlük / aylık / görüşme başına LLM harcama limiti.

namespace CustomerSupportBot.Application.Services.Budget;

public sealed class LlmBudgetOptions
{
    public const string SectionName = "LlmBudget";

    /// <summary>Limit nedeniyle LLM'siz yanıtlanan turun sonlanma sebebi (akış olayında).</summary>
    public const string TerminationReason = "budget_exceeded";

    /// <summary>Kapalıyken hiçbir kontrol yapılmaz ve sayaç tutulmaz.</summary>
    public bool Enabled { get; set; }

    /// <summary>UTC günü; <c>0</c> = sınırsız.</summary>
    public decimal DailyLimitUsd { get; set; }

    /// <summary>UTC ayı; <c>0</c> = sınırsız.</summary>
    public decimal MonthlyLimitUsd { get; set; }

    /// <summary>Bir görüşmeye atfedilen toplam; <c>0</c> = sınırsız.</summary>
    public decimal PerConversationLimitUsd { get; set; }

    /// <summary>Günlük/aylık uyarı eşiği (yüzde). %100'de ayrıca "limit doldu" uyarısı verilir.</summary>
    public int WarningThresholdPercent { get; set; } = 80;

    /// <summary>Uyarı e-postası alıcıları; <c>Email:Enabled</c> gerekir. Boşsa yalnızca log.</summary>
    public List<string> AlertEmails { get; set; } = [];

    /// <summary>Günlük/aylık limit aşıldığında müşteriye verilen yanıt.</summary>
    public string UnavailableMessage { get; set; } =
        "Otomatik asistan şu anda kullanılamıyor. Lütfen biraz sonra tekrar deneyin.";

    /// <summary>Görüşme başına limit aşıldığında müşteriye verilen yanıt (görüşme bir temsilciye aktarılır).</summary>
    public string ConversationLimitMessage { get; set; } =
        "Bu görüşmede otomatik yanıt sınırına ulaşıldı. Talebiniz bir müşteri temsilcisine iletildi; en kısa sürede size dönülecek.";
}
