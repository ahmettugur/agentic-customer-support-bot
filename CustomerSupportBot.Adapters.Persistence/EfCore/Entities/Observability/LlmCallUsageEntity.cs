namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;

/// <summary>observability.llm_call_usage tablosu — her LLM çağrısı için kalıcı maliyet kaydı.</summary>
public sealed class LlmCallUsageEntity
{
    public long Id { get; set; }
    public string Model { get; set; } = "";
    public string Provider { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public double DurationMs { get; set; }
    public DateTime CalledAt { get; set; }
}
