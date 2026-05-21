// Api/Models/WorkflowRequest.cs
// HTTP request DTO — Domain modelinin wire contract olarak inbound sınırı geçmesini engeller.
// WorkflowEndpoints bu DTO'yu bind eder, ardından Domain modeline dönüştürür.

namespace CustomerSupportBot.Api.Models;

/// <summary>
/// Workflow oluşturma/güncelleme için HTTP request DTO.
/// Domain modeli (WorkflowDefinition) doğrudan API sınırına maruz kalmaz.
/// </summary>
public sealed class WorkflowRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public List<string> TriggerKeywords { get; set; } = new();
    public Dictionary<string, string> InputPatterns { get; set; } = new();
    public List<WorkflowStepRequest> Steps { get; set; } = new();
}

/// <summary>
/// Workflow adımı için HTTP request DTO.
/// </summary>
public sealed class WorkflowStepRequest
{
    public string? Id { get; set; }
    public string Type { get; set; } = "Respond";
    public string? Label { get; set; }
    public string? Template { get; set; }
    public string? Tool { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string? StoreAs { get; set; }
    public string? Condition { get; set; }
    public int SkipNext { get; set; } = 1;
    public string? VariableName { get; set; }
    public string? VariableValue { get; set; }
}
