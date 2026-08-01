// Adapters.Agents/Team/SpecialistReasoningSchema.cs
// OpenAI/Azure OpenAI response_format=json_schema (strict) ile specialist ajanların
// (Product/Order/Complaint/HumanHandoff) çıktısını bu şemaya zorlamak için kullanılan
// DTO'lar. Domain.Model.SpecialistReasoning/PreToolCheck/PostToolReflection BİLEREK
// yeniden kullanılmadı — PostToolReflection.StatusEnum gibi salt-okunur computed
// property'ler JSON schema'ya sızıp modele anlamsız bir alan gösterirdi. Bu tipler
// yalnızca beklenen JSON şeklini tanımlar; asıl parse hâlâ SpecialistReasoningParser
// üzerinden yapılır (bkz. specialist agent dosyalarındaki not — Anthropic bridge'i
// ResponseFormat'ı okumadığı için parser'ın defensive fence/alan bazlı mantığı kalkmadı).

using System.Text.Json;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal static class SpecialistReasoningSchemaOptions
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal sealed class SpecialistReasoningSchema
{
    public PreToolCheckSchema? PreToolCheck { get; set; }
    public double ResultConfidence { get; set; }
    public string ResultNotes { get; set; } = "";
    public PostToolReflectionSchema? PostToolReflection { get; set; }
}

internal sealed class PreToolCheckSchema
{
    /// <summary>Yalnızca OrderAgent kullanır (6 aday tool arasından seçim gerekçesini netleştirir);
    /// diğer ajanlarda promptları bu alandan bahsetmez, model boş bırakır.</summary>
    public string? SelectedTool { get; set; }
    public List<string> RequiredParams { get; set; } = [];
    /// <summary>Complaint/HumanHandoff promptlarında kullanılır (ör. customer_id — otomatik türetilir,
    /// missingParams'a sayılmaz); parser tarafından okunmaz, yalnızca model muhakemesi için.</summary>
    public List<string> OptionalParams { get; set; } = [];
    public List<string> CollectedParams { get; set; } = [];
    public List<string> MissingParams { get; set; } = [];
    public bool CanProceed { get; set; }
    public string Reasoning { get; set; } = "";
    public double Confidence { get; set; } = 0.5;
}

internal sealed class PostToolReflectionSchema
{
    public bool TaskComplete { get; set; }
    public string Status { get; set; } = "";
    public string? HandoffSuggestion { get; set; }
    public string HandoffReason { get; set; } = "";
    public List<string> MissingContext { get; set; } = [];
    public string Summary { get; set; } = "";
}
