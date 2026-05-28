// Adapters.Telemetry/Chat/TelemetryChatClient.cs
// IChatClient için DelegatingChatClient — her LLM çağrısında:
//   1) ActivitySource üzerinden "ai.chat" span'i açar
//   2) UsageDetails'tan token sayılarını okur
//   3) ICostCalculatorPort ile USD maliyeti hesaplar
//   4) Meter counter'larına ve in-memory CostUsageStore'a yazar
//   5) Streaming ve non-streaming akışlarını ayrı işler

using System.Diagnostics;
using System.Runtime.CompilerServices;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LlmCallRecord = CustomerSupportBot.Application.Ports.Outbound.Observability.LlmCallRecord;

namespace CustomerSupportBot.Adapters.Telemetry.Chat;

public sealed class TelemetryChatClient : DelegatingChatClient
{
    private readonly ICostCalculatorPort _costCalculator;
    private readonly CostUsageStore _usageStore;
    private readonly ILlmCallPersistencePort? _persistence;
    private readonly string _modelHint;
    private readonly string _provider;
    private readonly ILogger<TelemetryChatClient> _logger;

    public TelemetryChatClient(
        IChatClient inner,
        ICostCalculatorPort costCalculator,
        CostUsageStore usageStore,
        string modelHint,
        string provider,
        ILogger<TelemetryChatClient> logger,
        ILlmCallPersistencePort? persistence = null) : base(inner)
    {
        _costCalculator = costCalculator;
        _usageStore = usageStore;
        _persistence = persistence;
        _modelHint = modelHint;
        _provider = provider;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = CustomerSupportTelemetry.StartLlmActivity("chat", _modelHint, _provider);
        var sw = Stopwatch.StartNew();
        ChatResponse? response = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
            sw.Stop();
            RecordSuccess(activity, response.Usage, sw.Elapsed.TotalMilliseconds, ResolveModel(response));
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure(activity, ex);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = CustomerSupportTelemetry.StartLlmActivity("chat.stream", _modelHint, _provider);
        var sw = Stopwatch.StartNew();
        UsageDetails? lastUsage = null;
        string? lastModel = null;

        IAsyncEnumerable<ChatResponseUpdate> stream;
        try
        {
            stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure(activity, ex);
            throw;
        }

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // UsageDetails ChatResponseUpdate üzerinde direkt yok — Contents içinde UsageContent olarak gelir
            foreach (var c in update.Contents)
            {
                if (c is UsageContent uc) lastUsage = uc.Details;
            }
            if (!string.IsNullOrEmpty(update.ModelId)) lastModel = update.ModelId;
            yield return update;
        }

        sw.Stop();
        RecordSuccess(activity, lastUsage, sw.Elapsed.TotalMilliseconds, lastModel ?? _modelHint);
    }

    // ─── Internals ───

    private static string ResolveModel(ChatResponse response)
    {
        return response.ModelId ?? string.Empty;
    }

    private void RecordSuccess(Activity? activity, UsageDetails? usage, double durationMs, string? actualModel)
    {
        var model = !string.IsNullOrEmpty(actualModel) ? actualModel! : _modelHint;
        long input = usage?.InputTokenCount ?? 0;
        long output = usage?.OutputTokenCount ?? 0;
        decimal cost = _costCalculator.CalculateCost(model, _provider, (int)input, (int)output);

        var modelTag = new KeyValuePair<string, object?>("ai.model", model);
        var providerTag = new KeyValuePair<string, object?>("ai.provider", _provider);

        CustomerSupportTelemetry.LlmCallsCounter.Add(1, modelTag, providerTag);
        if (input > 0) CustomerSupportTelemetry.InputTokensCounter.Add(input, modelTag, providerTag);
        if (output > 0) CustomerSupportTelemetry.OutputTokensCounter.Add(output, modelTag, providerTag);
        if (cost > 0) CustomerSupportTelemetry.CostUsdCounter.Add((double)cost, modelTag, providerTag);
        CustomerSupportTelemetry.LlmLatencyHistogram.Record(durationMs, modelTag, providerTag);

        _usageStore.Record(model, input, output, cost, durationMs);

        PersistAsync(model, input, output, cost, durationMs);

        if (activity != null)
        {
            activity.SetTag("ai.model.actual", model);
            activity.SetTag("ai.tokens.input", input);
            activity.SetTag("ai.tokens.output", output);
            activity.SetTag("ai.cost.usd", (double)cost);
            activity.SetTag("ai.duration.ms", durationMs);
            activity.SetStatus(ActivityStatusCode.Ok);
        }

        _logger.LogDebug(
            "LLM call recorded: model={Model} input={Input} output={Output} cost=${Cost:F6} duration={Ms:F0}ms",
            model, input, output, cost, durationMs);
    }

    private void PersistAsync(string model, long input, long output, decimal cost, double durationMs)
    {
        if (_persistence == null) return;
        var record = new LlmCallRecord(model, _provider, input, output, cost, durationMs, DateTime.UtcNow);
        _ = Task.Run(async () =>
        {
            try { await _persistence.RecordAsync(record); }
            catch { /* RecordAsync kendi içinde log'luyor */ }
        });
    }

    private void RecordFailure(Activity? activity, Exception ex)
    {
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddTag("error.type", ex.GetType().FullName);
        }
        _logger.LogWarning(ex, "LLM call failed (model={Model})", _modelHint);
    }
}
