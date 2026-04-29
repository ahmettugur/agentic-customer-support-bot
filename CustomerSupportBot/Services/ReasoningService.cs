// Services/ReasoningService.cs
// Kullanıcı sorgusu için açık (explicit) reasoning adımları üretir.
// ReasoningAgent rolünü workflow dışında çalıştırır — böylece group chat
// akışı bozulmaz ve reasoning çıktısı bağımsız olarak kullanıcıya sunulabilir.

using System.Runtime.CompilerServices;
using System.Text;
using CustomerSupportBot.Models;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Services;

/// <summary>
/// Reasoning adımlarını üretir ve yapılandırılmış formatta döner.
/// Group chat workflow'dan bağımsız çalışır — önce reasoning, sonra ana workflow.
/// Mesaj kurma, parse ve sanity check ayrı sınıflara delege edilir.
/// </summary>
public class ReasoningService
{
    private readonly ReasoningChatClient _reasoningClient;
    private readonly ILogger<ReasoningService> _logger;
    private readonly EntityVerifier _entityVerifier;
    private readonly ReasoningSanityChecker _sanityChecker;
    private readonly ReasoningMessageBuilder _messageBuilder;

    public ReasoningService(
        ReasoningChatClient reasoningClient,
        ILogger<ReasoningService> logger,
        PromptService prompts,
        EntityVerifier entityVerifier,
        ReasoningSanityChecker sanityChecker)
    {
        _reasoningClient = reasoningClient;
        _logger = logger;
        _entityVerifier = entityVerifier;
        _sanityChecker = sanityChecker;
        _messageBuilder = new ReasoningMessageBuilder(prompts);
    }

    /// <summary>
    /// Verilen sorgu için reasoning üretir. Tek seferlik (non-streaming) çağrı.
    /// </summary>
    public async Task<ReasoningResult> ReasonAsync(
        string query,
        AgentSession session,
        List<ChatMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var verified = _entityVerifier.Verify(query, session, history);
        var messages = _messageBuilder.Build(query, session, history, verified);

        try
        {
            var options = new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [WellKnown.ReasoningEffort.PropertyKey] = _reasoningClient.ReasoningEffort
                }
            };

            var response = await _reasoningClient.Client.GetResponseAsync(
                messages, options, cancellationToken);

            var text = response.Text ?? "";
            var result = ReasoningResultParser.Parse(text);

            // Sanity checker: reasoning çıktısını VerifiedEntities ile kıyaslayıp tutarsızlıkları işaretler.
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

    /// <summary>
    /// Reasoning'i streaming olarak üretir ve <see cref="StreamEvent"/> dizisi döner.
    /// Token'lar geldikçe reasoning_delta yayınlanır, sonunda reasoning_complete döner.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
        string query,
        AgentSession session,
        List<ChatMessage>? history = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamEvent(StreamEventTypes.ReasoningStart, null);

        var verified = _entityVerifier.Verify(query, session, history);
        var messages = _messageBuilder.Build(query, session, history, verified);

        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [WellKnown.ReasoningEffort.PropertyKey] = _reasoningClient.ReasoningEffort
            }
        };

        var buffer = new StringBuilder();
        string? streamError = null;

        IAsyncEnumerable<ChatResponseUpdate>? stream = null;
        try
        {
            stream = _reasoningClient.Client.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            streamError = ex.Message;
            _logger.LogWarning(ex, "Reasoning stream başlatılamadı");
        }

        if (stream != null)
        {
            // O4-mini internal thinking bittikten sonra JSON'u hızlı akıttığı için
            // token'lar arası minimum pacing uyguluyoruz.
            var pendingChunks = new StringBuilder();
            var lastEmit = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(100);
            var minInterval = TimeSpan.FromMilliseconds(20);

            await foreach (var update in EnumerateSafely(stream, cancellationToken))
            {
                if (update.Error != null)
                {
                    streamError = update.Error;
                    break;
                }

                var text = update.Text;
                if (string.IsNullOrEmpty(text)) continue;

                buffer.Append(text);
                pendingChunks.Append(text);

                var elapsed = DateTimeOffset.UtcNow - lastEmit;
                if (elapsed >= minInterval)
                {
                    var chunkText = pendingChunks.ToString();
                    pendingChunks.Clear();
                    yield return new StreamEvent(StreamEventTypes.ReasoningDelta,
                        new { text = chunkText });
                    lastEmit = DateTimeOffset.UtcNow;

                    await Task.Delay(minInterval, cancellationToken);
                }
            }

            // Kalan chunk'ı flush et
            if (pendingChunks.Length > 0)
            {
                yield return new StreamEvent(StreamEventTypes.ReasoningDelta,
                    new { text = pendingChunks.ToString() });
            }
        }

        // Final parse
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

    /// <summary>
    /// IAsyncEnumerable'ı güvenli şekilde tüketir, exception'ları error update'ine çevirir.
    /// Yield return catch bloğu içinde kullanılamadığı için bu helper gereklidir.
    /// </summary>
    private static async IAsyncEnumerable<SafeUpdate> EnumerateSafely(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                SafeUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        yield break;
                    update = new SafeUpdate(enumerator.Current.Text ?? "", null);
                }
                catch (Exception ex)
                {
                    update = new SafeUpdate("", ex.Message);
                }
                yield return update;
                if (update.Error != null) yield break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private record SafeUpdate(string Text, string? Error);
}
