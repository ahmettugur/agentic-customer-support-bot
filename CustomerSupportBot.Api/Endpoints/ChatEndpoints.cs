// Endpoints/ChatEndpoints.cs
// Chat endpoint registration — delegates all logic to orchestrator services.
// Routes: POST /chat/ (non-streaming), POST /chat/stream (SSE), GET /chat/events/{id} (persistent SSE)

using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;

namespace CustomerSupportBot.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat/", HandleChatAsync).RequireRateLimiting("chat");
        app.MapPost("/chat/stream", HandleChatStreamAsync).RequireRateLimiting("chat");
        app.MapGet("/chat/events/{sessionId}", HandleChatEventsAsync);
        return app;
    }

    /// <summary>
    /// Non-streaming chat: reasoning + workflow in sequence, returns single JSON.
    /// </summary>
    private static async Task<IResult> HandleChatAsync(
        ChatRequest request,
        CustomerSupportTeam team,
        ISessionManager sessionManager,
        ReasoningService reasoningService,
        IApprovalContextAccessor approvalContext,
        InputGuard inputGuard,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ChatEndpoints");
        var guardResult = inputGuard.Inspect(request.Query);
        if (guardResult.Verdict == InputGuardVerdict.Reject)
        {
            logger.LogWarning(
                "InputGuard rejected request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guardResult.Flags));
            return Results.Json(new
            {
                error = "input_blocked",
                message = guardResult.RejectionReason,
                flags = guardResult.Flags
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var safeQuery = guardResult.SanitizedInput;
        if (guardResult.Flags.Count > 0)
        {
            logger.LogInformation(
                "InputGuard flagged request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guardResult.Flags));
        }

        var session = sessionManager.GetOrCreateSession(request.SessionId);
        var sessionId = session.SessionId;
        var history = sessionManager.GetHistory(sessionId);

        var reasoning = await reasoningService.ReasonAsync(safeQuery, session, history);

        // Approval context'i set et — HITL tool onayı için gerekli
        using var approvalScope = approvalContext.SetScope(sessionId, null, safeQuery);
        var response = await team.RunAsync(safeQuery, history, session, reasoning);

        if (!string.IsNullOrWhiteSpace(reasoning.Intent) && reasoning.Intent != "bilinmiyor")
        {
            session.State.CurrentIntent = reasoning.Intent;
            sessionManager.UpdateSession(session);
        }

        sessionManager.AddExchange(sessionId, safeQuery, response);

        return Results.Json(new ChatResponse(response, sessionId, reasoning));
    }

    /// <summary>
    /// SSE streaming chat: reasoning → workflow → response deltas.
    /// Delegates execution to <see cref="ChatStreamOrchestrator"/>.
    /// </summary>
    private static async Task HandleChatStreamAsync(
        ChatRequest request,
        HttpResponse response,
        HttpContext httpContext,
        ChatStreamOrchestrator orchestrator,
        ISessionManager sessionManager,
        InputGuard inputGuard,
        ILoggerFactory loggerFactory)
    {
        SseWriter.WriteHeaders(response);
        var logger = loggerFactory.CreateLogger("ChatEndpoints");

        var guardResult = inputGuard.Inspect(request.Query);
        if (guardResult.Verdict == InputGuardVerdict.Reject)
        {
            logger.LogWarning(
                "InputGuard rejected stream request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guardResult.Flags));

            using var blockedSse = new SseForwarder(response, httpContext.RequestAborted);
            await blockedSse.WriteSessionAsync(request.SessionId ?? "unknown");
            await SseWriter.WriteEventAsync(
                response,
                "response_complete",
                new { content = guardResult.RejectionReason, blocked = true, flags = guardResult.Flags },
                httpContext.RequestAborted);
            return;
        }

        if (guardResult.Flags.Count > 0)
        {
            logger.LogInformation(
                "InputGuard flagged stream request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guardResult.Flags));
        }

        // Sanitize edilmiş sorguyla devam et
        var safeRequest = request with { Query = guardResult.SanitizedInput };

        using var sse = new SseForwarder(response, httpContext.RequestAborted);
        var session = sessionManager.GetOrCreateSession(safeRequest.SessionId);

        await sse.WriteSessionAsync(session.SessionId);
        await orchestrator.ExecuteAsync(safeRequest, session, sse, httpContext.RequestAborted);
    }

    /// <summary>
    /// Persistent SSE connection per session. Delegates to <see cref="ChatEventOrchestrator"/>.
    /// </summary>
    private static async Task HandleChatEventsAsync(
        string sessionId,
        HttpResponse response,
        HttpContext httpContext,
        ChatEventOrchestrator orchestrator)
    {
        SseWriter.WriteHeaders(response);

        using var sse = new SseForwarder(response, httpContext.RequestAborted);
        await orchestrator.ExecuteAsync(sessionId, sse, httpContext.RequestAborted);
    }
}
