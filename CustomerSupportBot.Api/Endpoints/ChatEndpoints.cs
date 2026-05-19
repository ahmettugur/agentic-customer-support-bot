// Endpoints/ChatEndpoints.cs
// Chat endpoint registration — delegates all logic to orchestrator services.
// Routes: POST /chat/ (non-streaming), POST /chat/stream (SSE), GET /chat/events/{id} (persistent SSE)

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
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
        IChatPort chatPort,
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

        if (guardResult.Flags.Count > 0)
        {
            logger.LogInformation(
                "InputGuard flagged request | session={Session} flags={Flags}",
                request.SessionId, string.Join(",", guardResult.Flags));
        }

        var safeRequest = request with { Query = guardResult.SanitizedInput };
        var response = await chatPort.HandleAsync(safeRequest);
        return Results.Json(response);
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

        var safeRequest = request with { Query = guardResult.SanitizedInput };

        using var sse = new SseForwarder(response, httpContext.RequestAborted);
        var session = sessionManager.GetOrCreate(safeRequest.SessionId);

        await sse.WriteSessionAsync(session.SessionId);
        // ChatStreamOrchestrator: HITL subscription, human mode, SSE-specific logic
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

