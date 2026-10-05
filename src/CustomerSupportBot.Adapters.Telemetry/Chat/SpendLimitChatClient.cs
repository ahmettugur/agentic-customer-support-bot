// Adapters.Telemetry/Chat/SpendLimitChatClient.cs
// IChatClient için DelegatingChatClient — LLM harcama limiti:
//   1) Çağrıdan önce limit kontrolü (aşıldıysa LlmBudgetExceededException, model çağrılmaz)
//   2) Yanıttan sonra maliyet kaydı (görüşme kimliği o anki kapsamdan)
// Telemetriden bağımsızdır: Telemetry:Enabled kapalıyken de limit uygulanır.

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Telemetry.Chat;

public sealed class SpendLimitChatClient(
    IChatClient inner,
    ILlmSpendGuard guard,
    ICostCalculatorPort costCalculator,
    string modelHint,
    string provider,
    ILlmCallAttribution? attribution = null) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sessionId = attribution?.CurrentSessionId;
        await EnsureWithinBudgetAsync(sessionId, cancellationToken);
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Record(response.ModelId, response.Usage, sessionId);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = attribution?.CurrentSessionId;
        await EnsureWithinBudgetAsync(sessionId, cancellationToken);

        UsageDetails? usage = null;
        string? model = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var c in update.Contents)
                if (c is UsageContent uc) usage = uc.Details;
            if (!string.IsNullOrEmpty(update.ModelId)) model = update.ModelId;
            yield return update;
        }
        Record(model, usage, sessionId);
    }

    private async Task EnsureWithinBudgetAsync(string? sessionId, CancellationToken ct)
    {
        if (await guard.CheckAsync(sessionId, ct) is { } exceeded)
            throw new LlmBudgetExceededException(exceeded);
    }

    /// <summary>
    /// Kayıt beklenmez: yanıt modele bağlı akışı Redis (ve eşik aşımında e-posta) gecikmesiyle yavaşlatmasın.
    /// <see cref="ILlmSpendGuard.RecordAsync"/> hata fırlatmaz. Görüşme kimliği çağrı anında alınmıştır.
    /// </summary>
    private void Record(string? actualModel, UsageDetails? usage, string? sessionId)
    {
        var model = string.IsNullOrEmpty(actualModel) ? modelHint : actualModel;
        var cost = costCalculator.CalculateCost(model, provider,
            (int)(usage?.InputTokenCount ?? 0), (int)(usage?.OutputTokenCount ?? 0));
        _ = guard.RecordAsync(cost, sessionId, CancellationToken.None);
    }
}
