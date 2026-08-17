// Api/Workers/KnowledgeBaseIngestor.cs
// Startup hosting adapter — uygulama açılışında KnowledgeBase ingest use case'ini tetikler.
// Use-case mantığı (chunking, change-detection, orkestrasyonu) Application katmanındadır
// (Application/Services/Memory/KnowledgeBaseIngestionService).

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Inbound;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Startup'ta KnowledgeBase ingest use case'ini tetikleyen ince hosting adapter.
/// AutoIngestOnStartup=false ise hiçbir şey yapmaz; use-case kendisi Enabled ve değişiklik kontrolü yapar.
/// </summary>
public sealed class KnowledgeBaseStartupService : IHostedService
{
    private readonly IMemoryPort _memory;
    private readonly SemanticMemoryOptions _options;
    private readonly ILogger<KnowledgeBaseStartupService> _logger;

    public KnowledgeBaseStartupService(
        IMemoryPort memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeBaseStartupService> logger)
    {
        _memory  = memory;
        _options = options.Value;
        _logger  = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (!_options.KnowledgeBase.AutoIngestOnStartup)
        {
            _logger.LogInformation("KnowledgeBase auto-ingest devre dışı (AutoIngestOnStartup=false).");
            return Task.CompletedTask;
        }

        return _memory.IngestAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
