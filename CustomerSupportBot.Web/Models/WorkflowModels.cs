using System.Text.Json;

namespace CustomerSupportBot.Web.Models;

public sealed class WorkflowListEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int Version { get; init; }
    public List<JsonElement>? Steps { get; init; }
    public int StepCount => Steps?.Count ?? 0;
}

public sealed record WorkflowListResponse(int Count, List<WorkflowListEntry> Items);
