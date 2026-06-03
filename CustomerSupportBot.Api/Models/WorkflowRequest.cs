// Api/Models/WorkflowRequest.cs
// HTTP request DTO — Domain modelinin wire contract olarak inbound sınırı geçmesini engeller.

namespace CustomerSupportBot.Api.Models;

public sealed class WorkflowRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? StartStepId { get; set; }
    public List<string> TriggerKeywords { get; set; } = new();
    public Dictionary<string, string> InputPatterns { get; set; } = new();
    public List<WorkflowStepRequest> Steps { get; set; } = new();
}

public sealed class WorkflowStepRequest
{
    public string? Id { get; set; }
    public string Type { get; set; } = "Respond";
    public string? Label { get; set; }

    // Navigation
    public string? Next { get; set; }
    public string? OnTrue { get; set; }
    public string? OnFalse { get; set; }

    // Respond
    public string? Template { get; set; }

    // Lookup
    public string? Tool { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string? StoreAs { get; set; }

    // Branch
    public string? Condition { get; set; }

    // SetVariable
    public string? VariableName { get; set; }
    public string? VariableValue { get; set; }
}
