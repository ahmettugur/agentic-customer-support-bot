// Application/Services/ChatPortService.cs
// IChatPort implementasyonu.
// Driving adapter (HTTP endpoint) bu servis üzerinden Core'u çağırır.
// Non-streaming ve streaming her iki kullanım senaryosunu orkestre eder.

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using ChatResponse = CustomerSupportBot.Domain.Model.ChatResponse;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// IChatPort implementasyonu — HTTP driving adapter'ının bağlandığı Application kapısı.
/// Oturum yönetimi, input doğrulama, reasoning ve agent workflow'u orkestre eder.
/// </summary>
public sealed class ChatPortService : IChatPort
{
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoning;
    private readonly ISessionRepository _sessions;
    private readonly InputGuard _inputGuard;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly ILogger<ChatPortService> _logger;

    public ChatPortService(
        IAgentTeamPort team,
        IReasoningPort reasoning,
        ISessionRepository sessions,
        InputGuard inputGuard,
        IApprovalContextAccessor approvalContext,
        ILogger<ChatPortService> logger)
    {
        _team = team;
        _reasoning = reasoning;
        _sessions = sessions;
        _inputGuard = inputGuard;
        _approvalContext = approvalContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default)
    {
        var guard = _inputGuard.Inspect(request.Query);
        if (guard.Verdict == InputGuardVerdict.Reject)
        {
            _logger.LogWarning(
                "InputGuard rejected request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guard.Flags));
            return new ChatResponse(guard.RejectionReason ?? "İstek engellendi.", request.SessionId ?? "", null);
        }

        var safeQuery = guard.SanitizedInput;
        if (guard.Flags.Count > 0)
            _logger.LogInformation(
                "InputGuard flagged request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guard.Flags));

        var session = _sessions.GetOrCreate(request.SessionId);
        var sessionId = session.SessionId;
        var history = _sessions.GetHistory(sessionId);

        var reasoningResult = await _reasoning.ReasonAsync(safeQuery, session, history, ct);

        using var approvalScope = _approvalContext.SetScope(sessionId, null, safeQuery);
        var response = await _team.RunAsync(safeQuery, history, session, reasoningResult);

        if (!string.IsNullOrWhiteSpace(reasoningResult.Intent) &&
            reasoningResult.Intent != WellKnown.Intents.Unknown)
        {
            session.State.CurrentIntent = reasoningResult.Intent;
            _sessions.Update(session);
        }

        _sessions.AddExchange(sessionId, safeQuery, response);

        return new ChatResponse(response, sessionId, reasoningResult);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var guard = _inputGuard.Inspect(request.Query);
        if (guard.Verdict == InputGuardVerdict.Reject)
        {
            _logger.LogWarning(
                "InputGuard rejected stream | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guard.Flags));
            yield return new StreamEvent(StreamEventTypes.ResponseComplete,
                new { content = guard.RejectionReason, blocked = true, flags = guard.Flags });
            yield break;
        }

        var safeQuery = guard.SanitizedInput;
        var session = _sessions.GetOrCreate(request.SessionId);
        var sessionId = session.SessionId;
        var history = _sessions.GetHistory(sessionId);

        ReasoningResult? reasoningResult = null;
        await foreach (var evt in _reasoning.ReasonStreamingAsync(safeQuery, session, history, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
            {
                reasoningResult = rr;
                if (!string.IsNullOrWhiteSpace(rr.Intent) && rr.Intent != WellKnown.Intents.Unknown)
                {
                    session.State.CurrentIntent = rr.Intent;
                    _sessions.Update(session);
                }
            }
        }

        using var approvalScope = _approvalContext.SetScope(sessionId, null, safeQuery);

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var evt in _team.RunStreamingAsync(safeQuery, history, session, reasoningResult, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is not null)
            {
                var text = TryExtractText(evt.Data);
                if (!string.IsNullOrEmpty(text))
                    responseBuilder.Append(text);
            }
        }

        var fullResponse = responseBuilder.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(fullResponse))
            _sessions.AddExchange(sessionId, safeQuery, fullResponse);
    }

    private static string? TryExtractText(object data)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
                return textProp.GetString();
        }
        catch { /* ignore */ }
        return null;
    }
}
