// Models/ToolResult.cs
// Tool-level reasoning zarfı.
// Tüm specialist tool'ları bu yapıyı döndürür. Böylece LLM, sonucu düz metin
// Yerine yapılandırılmış görür ve postToolReflection'da daha doğru sinyal üretir.
//
// Alanlar:
// - success           : Tool hedefine ulaştı mı
// - confidence        : Sonucun güven skoru (0.0-1.0)
// - message           : İnsan-dostu kısa özet (LLM ve kullanıcı için)
// - data              : Başarı durumundaki yapılandırılmış veri
// - error             : Hata detayları (taksonomi)
// - suggestedAction   : LLM/specialist için kısa öneri ("retry", "ask_user", "escalate")

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Tool çağrısının standart dönüş zarfı.
/// JSON olarak serialize edilir ve LLM'e yapılandırılmış sonuç olarak sunulur.
/// </summary>
public class ToolResult
{
    /// <summary>Tool hedefine ulaştı mı? false → error zorunlu dolu olmalı.</summary>
    public bool Success { get; set; }

    /// <summary>Sonucun güven skoru (0.0-1.0). Başarısız ise 0.</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>İnsan-dostu kısa özet (Türkçe). Specialist bunu kullanır.</summary>
    public string Message { get; set; } = "";

    /// <summary>Başarı durumundaki yapılandırılmış veri (sipariş, ürün vb.).</summary>
    public object? Data { get; set; }

    /// <summary>Başarısız ise dolu; taksonomi içerir.</summary>
    public ToolError? Error { get; set; }

    /// <summary>
    /// LLM/specialist için aksiyon önerisi. Olası değerler:
    /// "proceed" | "ask_user" | "retry" | "escalate" | "abort".
    /// </summary>
    public string SuggestedAction { get; set; } = ToolSuggestedActions.Proceed;

    // ─── Factory helper'ları ───

    public static ToolResult Ok(string message, object? data = null, double confidence = 1.0) =>
        new()
        {
            Success = true,
            Confidence = confidence,
            Message = message,
            Data = data,
            SuggestedAction = ToolSuggestedActions.Proceed
        };

    public static ToolResult ValidationError(string userMessage, params string[] missingFields) =>
        new()
        {
            Success = false,
            Confidence = 0.0,
            Message = userMessage,
            Error = new ToolError
            {
                Code = WellKnown.ToolErrorCodes.MissingRequiredField,
                Category = ToolErrorCategories.Validation,
                Message = userMessage,
                MissingFields = missingFields.ToList()
            },
            SuggestedAction = ToolSuggestedActions.AskUser
        };

    public static ToolResult NotFound(string code, string userMessage) =>
        new()
        {
            Success = false,
            Confidence = 0.0,
            Message = userMessage,
            Error = new ToolError
            {
                Code = code,
                Category = ToolErrorCategories.NotFound,
                Message = userMessage
            },
            SuggestedAction = ToolSuggestedActions.AskUser
        };

    public static ToolResult Conflict(string code, string userMessage, double partialConfidence = 0.3) =>
        new()
        {
            Success = false,
            Confidence = partialConfidence,
            Message = userMessage,
            Error = new ToolError
            {
                Code = code,
                Category = ToolErrorCategories.Conflict,
                Message = userMessage
            },
            SuggestedAction = ToolSuggestedActions.AskUser
        };

    public static ToolResult SystemError(string code, string userMessage) =>
        new()
        {
            Success = false,
            Confidence = 0.0,
            Message = userMessage,
            Error = new ToolError
            {
                Code = code,
                Category = ToolErrorCategories.System,
                Message = userMessage
            },
            SuggestedAction = ToolSuggestedActions.Escalate
        };
}

/// <summary>Tool hatasının yapılandırılmış detayı.</summary>
public class ToolError
{
    /// <summary>Makine-okunabilir hata kodu (ör. "ORDER_NOT_FOUND", "STOCK_INSUFFICIENT").</summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Hata kategorisi (taksonomisi).
    /// "validation" | "not_found" | "conflict" | "business_rule" | "system".
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>İnsan-dostu hata mesajı.</summary>
    public string Message { get; set; } = "";

    /// <summary>Validation hatalarında eksik olan alan adları.</summary>
    public List<string> MissingFields { get; set; } = new();
}

/// <summary>Kanonik tool hata kategorileri.</summary>
public static class ToolErrorCategories
{
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string BusinessRule = "business_rule";
    public const string System = "system";
}

/// <summary>Tool aksiyon önerisi sabitleri.</summary>
public static class ToolSuggestedActions
{
    public const string Proceed = "proceed";
    public const string AskUser = "ask_user";
    public const string Retry = "retry";
    public const string Escalate = "escalate";
    public const string Abort = "abort";
}
