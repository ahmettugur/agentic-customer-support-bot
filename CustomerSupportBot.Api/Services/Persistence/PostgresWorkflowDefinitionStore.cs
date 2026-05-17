// Services/Persistence/PostgresWorkflowDefinitionStore.cs
// Hibrit WorkflowDefinition store — in-memory cache + PostgreSQL write-through.
// Singleton servis ⇒ DbContext'i IDbContextFactory üzerinden açar.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Workflow;
using CustomerSupportBot.Api.Models.Workflow;
using CustomerSupportBot.Api.Services.Workflow;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed partial class PostgresWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresWorkflowDefinitionStore> _logger;
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private static readonly JsonSerializerOptions _json = new();

    public PostgresWorkflowDefinitionStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresWorkflowDefinitionStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public IReadOnlyList<WorkflowDefinition> GetAll()
    {
        EnsureHydrated();
        return _cache.Values
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public IReadOnlyList<WorkflowDefinition> GetActive()
    {
        EnsureHydrated();
        return _cache.Values.Where(d => d.IsActive).ToList();
    }

    public WorkflowDefinition? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureHydrated();
        return _cache.TryGetValue(id, out var d) ? d : null;
    }

    public WorkflowDefinition Upsert(WorkflowDefinition definition, string? updatedBy = null)
    {
        EnsureHydrated();

        if (string.IsNullOrWhiteSpace(definition.Id))
            definition.Id = Slugify(definition.Name);

        if (_cache.TryGetValue(definition.Id, out var existing))
        {
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

        try
        {
            UpsertAsync(definition).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WorkflowDefinition] DB UPSERT başarısız. Id={Id}", definition.Id);
            throw;
        }

        _cache[definition.Id] = definition;
        return definition;
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        EnsureHydrated();

        try
        {
            DeleteAsync(id).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WorkflowDefinition] DB DELETE başarısız. Id={Id}", id);
        }

        return _cache.TryRemove(id, out _);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(WorkflowDefinition def)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var entity = await ctx.WorkflowDefinitions.FirstOrDefaultAsync(e => e.Id == def.Id);
        var mapped = ToEntity(def);

        if (entity is null)
        {
            ctx.WorkflowDefinitions.Add(mapped);
        }
        else
        {
            entity.Name = mapped.Name;
            entity.Description = mapped.Description;
            entity.Version = mapped.Version;
            entity.IsActive = mapped.IsActive;
            entity.TriggerKeywordsJson = mapped.TriggerKeywordsJson;
            entity.InputPatternsJson = mapped.InputPatternsJson;
            entity.StepsJson = mapped.StepsJson;
            entity.UpdatedAt = mapped.UpdatedAt;
            entity.UpdatedBy = mapped.UpdatedBy;
        }

        await ctx.SaveChangesAsync();
    }

    private async Task DeleteAsync(string id)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var entity = await ctx.WorkflowDefinitions.FirstOrDefaultAsync(e => e.Id == id);
        if (entity != null)
        {
            ctx.WorkflowDefinitions.Remove(entity);
            await ctx.SaveChangesAsync();
        }
    }

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkflowDefinition] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.WorkflowDefinitions.AsNoTracking().ToListAsync();
        foreach (var e in rows)
            _cache[e.Id] = ToDomain(e);

        _logger.LogInformation("[WorkflowDefinition] Cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private static WorkflowDefinitionEntity ToEntity(WorkflowDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        Version = d.Version,
        IsActive = d.IsActive,
        TriggerKeywordsJson = JsonSerializer.Serialize(d.TriggerKeywords, _json),
        InputPatternsJson = JsonSerializer.Serialize(d.InputPatterns, _json),
        StepsJson = JsonSerializer.Serialize(d.Steps, _json),
        CreatedAt = d.CreatedAt == default ? DateTime.UtcNow : d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        UpdatedBy = d.UpdatedBy
    };

    private static WorkflowDefinition ToDomain(WorkflowDefinitionEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        Version = e.Version,
        IsActive = e.IsActive,
        TriggerKeywords = JsonSerializer.Deserialize<List<string>>(e.TriggerKeywordsJson, _json) ?? new(),
        InputPatterns = JsonSerializer.Deserialize<Dictionary<string, string>>(e.InputPatternsJson, _json) ?? new(),
        Steps = JsonSerializer.Deserialize<List<WorkflowStep>>(e.StepsJson, _json) ?? new(),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        UpdatedBy = e.UpdatedBy
    };

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlnumRegex();

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString("N")[..8];
        var lower = name.Trim().ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
            .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var slug = NonAlnumRegex().Replace(lower, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}
