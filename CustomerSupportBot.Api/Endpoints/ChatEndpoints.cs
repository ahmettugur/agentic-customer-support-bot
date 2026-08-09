// Endpoints/ChatEndpoints.cs
// Chat endpoint registration — delegates all logic to orchestrator services.
// Routes: POST /chat/ (non-streaming), POST /chat/stream (SSE), GET /chat/events/{id} (persistent SSE)

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat/", HandleChatAsync).RequireRateLimiting("chat").RequireAuthorization("Customer");
        app.MapPost("/chat/stream", HandleChatStreamAsync).RequireRateLimiting("chat").RequireAuthorization("Customer");
        app.MapGet("/chat/events/{sessionId}", HandleChatEventsAsync).RequireAuthorization("Customer");
        app.MapGet("/chat-sessions/{sessionId}/approvals/unseen", HandleGetUnseenApprovalsAsync).RequireAuthorization("Customer");
        app.MapPost("/chat-sessions/{sessionId}/approvals/{id}/seen", HandleMarkApprovalSeenAsync).RequireAuthorization("Customer");
        app.MapGet("/customer/approvals/history", HandleGetApprovalHistoryAsync).RequireAuthorization("Customer");
        return app;
    }

    /// <summary>
    /// Non-streaming chat: reasoning + workflow in sequence, returns single JSON.
    /// </summary>
    private static async Task<IResult> HandleChatAsync(
        ChatRequest request,
        HttpContext httpContext,
        IChatPort chatPort,
        IInputGuard inputGuard,
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

        var safeRequest = request with
        {
            Query = guardResult.SanitizedInput,
            CustomerId = ResolveAuthenticatedCustomerId(httpContext)
        };
        // İstemci bağlantıyı keserse reasoning/workflow zinciri de iptal edilir —
        // aksi halde LLM çağrısı WorkflowGuards:TimeoutSeconds süresince boşa çalışır.
        var response = await chatPort.HandleAsync(safeRequest, httpContext.RequestAborted);
        return Results.Json(response);
    }

    /// <summary>
    /// SSE streaming chat: reasoning → workflow → response deltas.
    /// Pure transport adapter: pipes IChatPort.HandleStreamAsync events to SSE.
    /// HITL (approval/escalation) events are bridged via HitlStreamSubscription.
    /// </summary>
    private static async Task HandleChatStreamAsync(
        ChatRequest request,
        HttpResponse response,
        HttpContext httpContext,
        IChatPort chatPort,
        IHitlEventPort hitlEvents,
        IInputGuard inputGuard,
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

        var safeRequest = request with
        {
            Query = guardResult.SanitizedInput,
            CustomerId = ResolveAuthenticatedCustomerId(httpContext)
        };

        using var sse = new SseForwarder(response, httpContext.RequestAborted);

        // Session ID is resolved by IChatPort and carried in the first Session event.
        // HITL subscription is wired up when that event arrives (before workflow begins).
        var resolvedSessionId = "";
        IHitlEventSubscription? hitlSubscription = null;
        try
        {
            await foreach (var evt in chatPort.HandleStreamAsync(safeRequest, httpContext.RequestAborted))
            {
                await sse.WriteAsync(evt.Type, evt.Data);

                if (hitlSubscription == null && evt.Type == StreamEventTypes.Session)
                {
                    resolvedSessionId = evt.Data is SessionEventPayload sp
                        ? sp.SessionId
                        : ExtractSessionId(evt.Data); // fallback: eski format uyumu
                    hitlSubscription = hitlEvents.Subscribe(
                        resolvedSessionId,
                        (eventType, eventData) => sse.WriteAsync(eventType, eventData));
                }
            }
        }
        finally
        {
            hitlSubscription?.Dispose();
        }

        await sse.WriteDoneAsync(resolvedSessionId);
    }

    /// <summary>
    /// Onay gerektiren tool'ların (sipariş/iade/iptal/şikayet) customerId'yi kullanıcının
    /// yazdığı metinden değil, kimlik doğrulanmış JWT claim'inden almasını sağlar — client
    /// body'sindeki hiçbir alandan customerId GÜVENİLİR olarak alınmaz.
    /// </summary>
    private static string? ResolveAuthenticatedCustomerId(HttpContext httpContext) =>
        httpContext.User.FindFirst("linked_customer_id")?.Value;

    private static string ExtractSessionId(object? data)
    {
        if (data == null) return "";
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("sessionId", out var prop))
                return prop.GetString() ?? "";
        }
        catch { /* ignore */ }
        return "";
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

    /// <summary>
    /// Kullanıcı chat'e (yeni sekme/sayfa yenileme sonrası) döndüğünde, bağlı değilken
    /// kaçırdığı onay sonuçlarını (bloklamayan onay modeli — bkz. ApprovalGateService)
    /// çekmek için. Badge/bildirim UI'ı sayfa açılışında bunu çağırır.
    /// </summary>
    private static IResult HandleGetUnseenApprovalsAsync(string sessionId, IApprovalQueue approvals)
    {
        var unseen = approvals.GetUnseenForSession(sessionId)
            .Select(r => new
            {
                id = r.Id,
                toolName = r.ToolName,
                status = r.Status.ToString().ToLowerInvariant(),
                decisionReason = r.DecisionReason,
                executionResult = r.ExecutionResult,
                decidedAt = r.DecidedAt
            });
        return Results.Ok(unseen);
    }

    private static async Task<IResult> HandleMarkApprovalSeenAsync(
        string sessionId, string id, IApprovalQueue approvals, CancellationToken ct)
    {
        var request = approvals.Get(id);
        if (request is null || request.SessionId != sessionId) return Results.NotFound();

        await approvals.MarkSeenAsync(id, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Kalıcı "geçmiş işlemlerim" görünümü — bekleyen/onaylanmış/reddedilmiş fark etmeksizin
    /// bu müşterinin TÜM onay taleplerini döner. Unseen endpoint'inin aksine görüldükten sonra
    /// da listede kalmaya devam eder (badge sayacına dahil değil, salt-okunur bir geçmiş).
    /// customerId route/body'den değil JWT claim'inden okunur — başka bir müşterinin
    /// geçmişini URL değiştirerek görme ihtimali yok.
    /// </summary>
    private static IResult HandleGetApprovalHistoryAsync(HttpContext httpContext, IApprovalQueue approvals)
    {
        var customerId = ResolveAuthenticatedCustomerId(httpContext);
        if (string.IsNullOrWhiteSpace(customerId)) return Results.Ok(Array.Empty<object>());

        var history = approvals.GetHistoryForCustomer(customerId)
            .Select(r => new
            {
                id = r.Id,
                toolName = r.ToolName,
                status = r.Status.ToString().ToLowerInvariant(),
                decisionReason = r.DecisionReason,
                executionResult = r.ExecutionResult,
                requestedAt = r.RequestedAt,
                decidedAt = r.DecidedAt
            });
        return Results.Ok(history);
    }
}
