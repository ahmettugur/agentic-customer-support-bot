namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Semantic memory yazma port'u — adapter'ların episodik bellek yazması için.
/// </summary>
public interface ISemanticMemoryWriter
{
    bool Enabled { get; }
    Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
        string finalResponse, string? intent, int? rating, CancellationToken ct = default);
}
