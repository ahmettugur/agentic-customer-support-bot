// Services/Workflow/InMemoryWorkflowDefinitionStore.cs

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model.Workflow;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public partial class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _defs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkflowDefinition> GetAll() =>
        _defs.Values.OrderByDescending(d => d.IsActive).ThenBy(d => d.Name).ToList();

    public IReadOnlyList<WorkflowDefinition> GetActive() =>
        _defs.Values.Where(d => d.IsActive).ToList();

    public WorkflowDefinition? Get(string id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : _defs.TryGetValue(id, out var d) ? d : null;

    public WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            definition.Id = Slugify(definition.Name);

        if (_defs.TryGetValue(definition.Id, out var existing))
        {
            // Versiyonu otomatik artır (sadece content değiştiyse)
            definition.CreatedAt = existing.CreatedAt;
            definition.Version = existing.Version + 1;
        }
        else if (definition.Version <= 0)
        {
            definition.Version = 1;
        }

        definition.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(updatedBy))
            definition.UpdatedBy = updatedBy;

        _defs[definition.Id] = definition;
        return definition;
    }

    public bool Delete(string id) => _defs.TryRemove(id, out _);

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlnumRegex();

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString("N")[..8];
        var lower = name.Trim().ToLowerInvariant()
            .Replace('\u0131', 'i').Replace('\u011f', 'g').Replace('\u00fc', 'u')
            .Replace('\u015f', 's').Replace('\u00f6', 'o').Replace('\u00e7', 'c');
        var slug = NonAlnumRegex().Replace(lower, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}

