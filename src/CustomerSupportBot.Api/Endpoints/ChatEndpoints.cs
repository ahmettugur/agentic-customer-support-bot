// Endpoints/ChatEndpoints.cs
// Chat endpoint registration — delegates all logic to orchestrator services.
// Routes: POST /chat/ (non-streaming), POST /chat/stream (SSE), GET /chat/events/{id} (persistent SSE)

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;

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
        ChatRequestBody request,
        HttpContext httpContext,
        IChatPort chatPort,
        IInputGuard inputGuard,
        ISessionManager sessions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ChatEndpoints");

        if (!await IsSessionAccessibleAsync(request.SessionId, httpContext, sessions))
        {
            logger.LogWarning("Oturum sahiplik ihlali reddedildi | session={Session}", request.SessionId);
            return SessionForbidden();
        }

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

        var safeRequest = new ChatRequest(
            guardResult.SanitizedInput,
            request.SessionId,
            CustomerId: ResolveAuthenticatedCustomerId(httpContext));
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
        ChatRequestBody request,
        HttpResponse response,
        HttpContext httpContext,
        IChatPort chatPort,
        IHitlEventPort hitlEvents,
        IInputGuard inputGuard,
        ISessionManager sessions,
        ILoggerFactory loggerFactory)
    {
        SseWriter.WriteHeaders(response);
        var logger = loggerFactory.CreateLogger("ChatEndpoints");

        // SSE'de header'lar yazıldıktan sonra HTTP durum kodu değiştirilemez; bu yüzden
        // ihlal, akışın içinde bir hata olayı olarak bildirilir ve tur hiç başlamaz.
        if (!await IsSessionAccessibleAsync(request.SessionId, httpContext, sessions))
        {
            logger.LogWarning("Oturum sahiplik ihlali reddedildi (stream) | session={Session}", request.SessionId);
            using var forbiddenSse = new SseForwarder(response, httpContext.RequestAborted);
            await forbiddenSse.WriteSessionAsync(request.SessionId ?? "unknown");
            await SseWriter.WriteEventAsync(
                response,
                "response_complete",
                new { content = "Bu oturuma erişim yetkiniz yok.", blocked = true, error = "session_forbidden" },
                httpContext.RequestAborted);
            return;
        }

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

        var safeRequest = new ChatRequest(
            guardResult.SanitizedInput,
            request.SessionId,
            CustomerId: ResolveAuthenticatedCustomerId(httpContext));

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

    /// <summary>
    /// <c>sessionId</c> istemciden gelir — URL'de ya da gövdede. Kimlik doğrulaması "bu kişi
    /// bir müşteri mi" sorusunu yanıtlar, "bu oturum onun mu" sorusunu değil. Bu kontrol
    /// olmadan müşteri B, müşteri A'nın oturum kimliğini vererek A'nın konuşma geçmişini,
    /// canlı olay akışını ve onay bildirimlerini okuyabilirdi.
    ///
    /// <para>
    /// Oturum henüz yoksa veya kimseye bağlı değilse erişim serbesttir — ilk temas onu
    /// çağırana bağlar (bkz. <c>SessionIdentityBinder</c>).
    /// </para>
    /// </summary>
    private static Task<bool> IsSessionAccessibleAsync(
        string? sessionId, HttpContext httpContext, ISessionManager sessions) =>
        SessionIdentityBinder.IsAccessibleAsync(
            sessionId, ResolveAuthenticatedCustomerId(httpContext), sessions, httpContext.RequestAborted);

    private static IResult SessionForbidden() =>
        Results.Json(new
        {
            error = "session_forbidden",
            message = "Bu oturuma erişim yetkiniz yok."
        }, statusCode: StatusCodes.Status403Forbidden);

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
        ChatEventOrchestrator orchestrator,
        ISessionManager sessions)
    {
        // Bu uç, oturumun TÜM canlı olaylarını yayınlar (bot yanıtları, onay sonuçları,
        // temsilci mesajları). Sahiplik kontrolü olmadan başka bir müşterinin konuşması
        // canlı olarak dinlenebilirdi.
        if (!await IsSessionAccessibleAsync(sessionId, httpContext, sessions))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        SseWriter.WriteHeaders(response);

        using var sse = new SseForwarder(response, httpContext.RequestAborted);

        // Açılıştaki kontrol tek başına yetmez: henüz kimseye bağlı OLMAYAN bir oturuma abone
        // olmak serbesttir, ama oturum sonradan başka bir müşteriye bağlanabilir. Bu delege
        // her olay yazımından önce yeniden çalışır.
        await orchestrator.ExecuteAsync(
            sessionId,
            sse,
            _ => IsSessionAccessibleAsync(sessionId, httpContext, sessions),
            httpContext.RequestAborted);
    }

    /// <summary>
    /// Kullanıcı chat'e (yeni sekme/sayfa yenileme sonrası) döndüğünde, bağlı değilken
    /// kaçırdığı onay sonuçlarını (bloklamayan onay modeli — bkz. ApprovalGateService)
    /// çekmek için. Badge/bildirim UI'ı sayfa açılışında bunu çağırır.
    /// </summary>
    private static async Task<IResult> HandleGetUnseenApprovalsAsync(
        string sessionId, HttpContext httpContext, IApprovalQueue approvals, ISessionManager sessions,
        CancellationToken ct)
    {
        if (!await IsSessionAccessibleAsync(sessionId, httpContext, sessions)) return SessionForbidden();

        var customerId = ResolveAuthenticatedCustomerId(httpContext);
        if (string.IsNullOrWhiteSpace(customerId)) return Results.Ok(Array.Empty<object>());

        var unseen = (await approvals.GetUnseenForSessionAsync(sessionId, customerId, ct))
            .Select(r => new
            {
                id = r.Id,
                toolName = r.ToolName,
                status = r.Status.ToString().ToLowerInvariant(),
                decisionReason = r.DecisionReason,
                executionResult = r.ExecutionResult,
                executionStatus = r.ExecutionStatus.ToString().ToLowerInvariant(),
                decidedAt = r.DecidedAt
            });
        return Results.Ok(unseen);
    }

    private static async Task<IResult> HandleMarkApprovalSeenAsync(
        string sessionId, string id, HttpContext httpContext,
        IApprovalQueue approvals, ISessionManager sessions, CancellationToken ct)
    {
        if (!await IsSessionAccessibleAsync(sessionId, httpContext, sessions)) return SessionForbidden();

        // Get() DEĞİL: unseen listesi kalıcı depodan cevaplanıyor, bu yüzden orada görünen bir
        // kayıt cache'de olmayabilir (cache açık kayıtlar + son N kararı tutar). Cache'e bakan
        // bir kontrol, listelenen eski bir bildirim için 404 döndürür ve bildirim her girişte
        // yeniden "görülmemiş" olarak çıkar.
        var request = await approvals.GetAsync(id, ct);
        if (request is null || request.SessionId != sessionId) return Results.NotFound();

        // Kaydın müşterisi de doğrulanır. Oturum sahipliği tek başına yetmez: onay kayıtları
        // oturumdan bağımsız yaşar ve oturum sahipliği kontrolü var olmayan oturumlara izin
        // verir, dolayısıyla silinmiş bir oturumun id'sini bilen biri buradan geçebilirdi.
        var callerCustomerId = ResolveAuthenticatedCustomerId(httpContext);
        if (string.IsNullOrWhiteSpace(callerCustomerId) ||
            !string.Equals(request.CustomerId, callerCustomerId, StringComparison.Ordinal))
            return Results.NotFound();

        // Yazma başarısızsa 204 dönmek istemciyi yanıltır: bildirimi okundu sayar ama kayıt
        // işaretlenmemiştir, aynı bildirim her girişte tekrar çıkar.
        if (!await approvals.MarkSeenAsync(id, ct))
            return Results.Problem(
                title: "Bildirim görüldü olarak işaretlenemedi",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.NoContent();
    }

    /// <summary>
    /// Kalıcı "geçmiş işlemlerim" görünümü — bekleyen/onaylanmış/reddedilmiş fark etmeksizin
    /// bu müşterinin TÜM onay taleplerini döner. Unseen endpoint'inin aksine görüldükten sonra
    /// da listede kalmaya devam eder (badge sayacına dahil değil, salt-okunur bir geçmiş).
    /// customerId route/body'den değil JWT claim'inden okunur — başka bir müşterinin
    /// geçmişini URL değiştirerek görme ihtimali yok.
    /// </summary>
    private static async Task<IResult> HandleGetApprovalHistoryAsync(
        HttpContext httpContext, IApprovalQueue approvals, CancellationToken ct)
    {
        var customerId = ResolveAuthenticatedCustomerId(httpContext);
        if (string.IsNullOrWhiteSpace(customerId)) return Results.Ok(Array.Empty<object>());

        var history = (await approvals.GetHistoryForCustomerAsync(customerId, ct: ct))
            .Select(r => new
            {
                id = r.Id,
                toolName = r.ToolName,
                status = r.Status.ToString().ToLowerInvariant(),
                decisionReason = r.DecisionReason,
                executionResult = r.ExecutionResult,
                executionStatus = r.ExecutionStatus.ToString().ToLowerInvariant(),
                requestedAt = r.RequestedAt,
                decidedAt = r.DecidedAt
            });
        return Results.Ok(history);
    }
}
