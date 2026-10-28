// Adapters.Agents/UsageLabelingChatClient.cs
// Bu istemciden geçen LLM çağrılarının token kullanımını bir etiketle (Reasoning, PlanningAgent, ...) tur biriktiricisine yazar.

using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Her ajan kendi sarmalayıcısını alır; böylece ortak model istemcisinin arkasında "kim harcadı" bilgisi kaybolmaz.
/// Kayıt, çağrının sonunda yapılır (akışta son <see cref="UsageContent"/>); kullanım bilgisi yoksa yazılmaz.
/// </summary>
public sealed class UsageLabelingChatClient(IChatClient inner, string label, ITurnUsageTracker tracker) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var c in update.Contents)
                if (c is UsageContent uc) usage = uc.Details;
            yield return update;
        }
        Record(usage);
    }

    private void Record(UsageDetails? usage)
    {
        var input = usage?.InputTokenCount ?? 0;
        var output = usage?.OutputTokenCount ?? 0;
        if (input > 0 || output > 0) tracker.Record(label, input, output);
    }
}
