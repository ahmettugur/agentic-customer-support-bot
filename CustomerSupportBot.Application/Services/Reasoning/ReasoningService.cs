// Application/Services/ReasoningService.cs
// Kullanıcı sorgusu için açık (explicit) reasoning adımları üretir.

using System.Runtime.CompilerServices;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Reasoning;

/// <summary>
/// Reasoning adımlarını üretir ve yapılandırılmış formatta döner.
/// Group chat workflow'dan bağımsız çalışır — önce reasoning, sonra ana workflow.
/// </summary>
public class ReasoningService : IReasoningPort
{
    private readonly IReasoningChatClient _reasoningClient;
    private readonly ILogger<ReasoningService> _logger;
    private readonly EntityVerifier _entityVerifier;
    private readonly ReasoningSanityChecker _sanityChecker;
    private readonly ReasoningMessageBuilder _messageBuilder;

    public ReasoningService(
        IReasoningChatClient reasoningClient,
        ILogger<ReasoningService> logger,
        IPromptRepository prompts,
        EntityVerifier entityVerifier,
        ReasoningSanityChecker sanityChecker)
    {
        _reasoningClient = reasoningClient;
        _logger = logger;
        _entityVerifier = entityVerifier;
        _sanityChecker = sanityChecker;
        _messageBuilder = new ReasoningMessageBuilder(prompts);
    }

    public async Task<ReasoningResult> ReasonAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        CancellationToken ct = default)
    {
        var verified = _entityVerifier.Verify(query, session, history);
        var messages = _messageBuilder.Build(query, session, history, verified);

        try
        {
            var text = await _reasoningClient.CompleteAsync(messages, ct);
            var result = ReasoningResultParser.Parse(text);
            result.SanityIssues = _sanityChecker.Check(result, verified);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reasoning başarısız, boş reasoning ile devam ediliyor");
            return new ReasoningResult
            {
                Analysis = WellKnown.FallbackMessages.ReasoningUnavailable,
                Steps = new List<ReasoningStep>(),
                Intent = session.State.CurrentIntent ?? WellKnown.Intents.Unknown,
                RequiredInfo = new List<string>(),
                Confidence = WellKnown.Confidence.Low,
                ConfidenceScore = 0.3,
                NextAction = "workflow'a düşük güvenle devam et"
            };
        }
    }

    public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamEvent(StreamEventTypes.ReasoningStart, null);

        var verified = _entityVerifier.Verify(query, session, history);
        var messages = _messageBuilder.Build(query, session, history, verified);

        var buffer = new StringBuilder();
        string? streamError = null;

        IAsyncEnumerable<string>? stream = null;
        try
        {
            stream = _reasoningClient.StreamAsync(messages, ct);
        }
        catch (Exception ex)
        {
            streamError = ex.Message;
            _logger.LogWarning(ex, "Reasoning stream başlatılamadı");
        }

        if (stream != null)
        {
            var pendingChunks = new StringBuilder();
            var lastEmit = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(100);
            var minInterval = TimeSpan.FromMilliseconds(20);

            await foreach (var (chunk, error) in EnumerateSafely(stream, ct))
            {
                if (error != null)
                {
                    streamError = error;
                    break;
                }

                if (string.IsNullOrEmpty(chunk)) continue;

                buffer.Append(chunk);
                pendingChunks.Append(chunk);

                var elapsed = DateTimeOffset.UtcNow - lastEmit;
                if (elapsed >= minInterval)
                {
                    var chunkText = pendingChunks.ToString();
                    pendingChunks.Clear();
                    yield return new StreamEvent(StreamEventTypes.ReasoningDelta, new TextDeltaPayload(chunkText));
                    lastEmit = DateTimeOffset.UtcNow;
                    await Task.Delay(minInterval, ct);
                }
            }

            if (pendingChunks.Length > 0)
                yield return new StreamEvent(StreamEventTypes.ReasoningDelta, new TextDeltaPayload(pendingChunks.ToString()));
        }

        var fullText = buffer.ToString();
        ReasoningResult result;

        if (!string.IsNullOrWhiteSpace(streamError) || string.IsNullOrWhiteSpace(fullText))
        {
            result = new ReasoningResult
            {
                Analysis = WellKnown.FallbackMessages.ReasoningIncomplete,
                Steps = new List<ReasoningStep>(),
                ConfidenceScore = 0.3,
                Intent = session.State.CurrentIntent ?? WellKnown.Intents.Unknown,
                RequiredInfo = new List<string>(),
                Confidence = WellKnown.Confidence.Low
            };
        }
        else
        {
            result = ReasoningResultParser.Parse(fullText);
        }

        result.SanityIssues = _sanityChecker.Check(result, verified);

        yield return new StreamEvent(StreamEventTypes.ReasoningComplete, result);
    }

    private static async IAsyncEnumerable<(string Chunk, string? Error)> EnumerateSafely(
        IAsyncEnumerable<string> stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                (string Chunk, string? Error) item;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        yield break;
                    item = (enumerator.Current, null);
                }
                catch (Exception ex)
                {
                    item = ("", ex.Message);
                }
                yield return item;
                if (item.Error != null) yield break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
