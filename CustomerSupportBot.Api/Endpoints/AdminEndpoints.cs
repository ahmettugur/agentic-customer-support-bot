// Endpoints/AdminEndpoints.cs
// HITL admin endpoint'leri:
//   - GET   /approvals/pending            : Onay bekleyen tool çağrıları
//   - GET   /approvals/recent?count=50    : Son N karar (history)
//   - GET   /approvals/{id}               : Tek request
//   - POST  /approvals/{id}/approve       : Onayla
//   - POST  /approvals/{id}/reject        : Reddet (body: { reason? })
//   - GET   /escalations/open             : Açık eskalasyonlar
//   - GET   /escalations/recent?count=50  : Son N
//   - GET   /escalations/{id}             : Tek kayıt
//   - POST  /escalations/{id}/acknowledge : Temsilci bu işi aldı
//   - POST  /escalations/{id}/resolve     : Çöz (body: { assignedTo?, resolution? })
//   - POST  /escalations/{id}/dismiss     : Reddet/yanlış eskalasyon
//
// HITL Live Takeover (chat-sessions):
//   - GET   /chat-sessions/active             : Şu an Human modda olan session'lar
//   - GET   /chat-sessions/{sid}/state        : Tek session kip + temsilci bilgisi
//   - GET   /chat-sessions/{sid}/history      : Bridge history (Bot+User+Admin+System)
//   - POST  /chat-sessions/{sid}/takeover     : (body: { humanAgent? }) → Human moda al
//   - POST  /chat-sessions/{sid}/release      : Bot moda döndür
//   - POST  /chat-sessions/{sid}/messages     : (body: { text, humanAgent? }) admin yanıtı
//   - GET   /chat-sessions/{sid}/subscribe    : SSE — bu session'a gelen user mesajları
//
// Production'da bu endpoint'lerin önüne auth (admin role) gelmelidir.

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── APPROVALS ───
        app.MapGet("/approvals/pending", async (IApprovalPort approvals, CancellationToken ct) =>
            Results.Json(await approvals.GetPendingAsync(ct)));

        app.MapGet("/approvals/recent", async (IApprovalPort approvals, CancellationToken ct, int count = 50) =>
            Results.Json(await approvals.GetRecentAsync(count, ct)));

        app.MapGet("/approvals/{id}", (string id, IApprovalPort approvals) =>
        {
            var req = approvals.Get(id);
            return req == null ? Results.NotFound() : Results.Json(req);
        });

        app.MapPost("/approvals/{id}/approve",
            async (string id, ApprovalDecisionInput? body, IApprovalPort approvals, CancellationToken ct) =>
        {
            var req = approvals.Get(id);
            if (req == null)
            {
                return Results.NotFound(new { error = "Request bulunamadı." });
            }

            // Yüksek riskli yan etkili tool'larda gerekçe (audit trail) zorunlu.
            // Diğerlerinde opsiyonel — admin akışını yavaşlatmamak için.
            if (WellKnown.HighRiskTools.Contains(req.ToolName) &&
                string.IsNullOrWhiteSpace(body?.Reason))
            {
                return Results.BadRequest(new
                {
                    error = "approval_reason_required",
                    message = $"'{req.ToolName}' yüksek riskli bir işlem; onay için gerekçe zorunludur."
                });
            }

            var ok = await approvals.DecideAsync(id, approved: true,
                decidedBy: body?.DecidedBy ?? WellKnown.Defaults.Admin,
                reason: body?.Reason, ct: ct);
            return ok
                ? Results.Json(new { id, status = "approved" })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        app.MapPost("/approvals/{id}/reject",
            async (string id, ApprovalDecisionInput? body, IApprovalPort approvals, CancellationToken ct) =>
        {
            var ok = await approvals.DecideAsync(id, approved: false,
                decidedBy: body?.DecidedBy ?? WellKnown.Defaults.Admin,
                reason: body?.Reason ?? "Admin reddetti", ct: ct);
            return ok
                ? Results.Json(new { id, status = "rejected" })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        // ─── ESCALATIONS ───
        app.MapGet("/escalations/open", (IEscalationPort escalations) =>
            Results.Json(escalations.GetOpen()));

        app.MapGet("/escalations/recent", (IEscalationPort escalations, int count = 50) =>
            Results.Json(escalations.GetRecent(count)));

        app.MapGet("/escalations/{id}", (string id, IEscalationPort escalations) =>
        {
            var esc = escalations.Get(id);
            return esc == null ? Results.NotFound() : Results.Json(esc);
        });

        app.MapPost("/escalations/{id}/acknowledge",
            (string id, EscalationDecisionInput? body, IEscalationPort escalations, IChatSessionPort chatSessions) =>
        {
            var ok = escalations.Decide(id, WellKnown.EscalationActions.Acknowledge,
                assignedTo: body?.AssignedTo);
            if (!ok)
            {
                return Results.NotFound(new { error = "Escalation bulunamadı veya zaten karara bağlandı." });
            }

            // Müşteriye bildirim — temsilci üstlendi, daha sonra iletişime geçilecek
            var esc = escalations.Get(id);
            if (!string.IsNullOrEmpty(esc?.SessionId))
            {
                var agentLabel = string.IsNullOrWhiteSpace(body?.AssignedTo)
                    ? "Bir temsilcimiz"
                    : $"Temsilcimiz {body!.AssignedTo}";
                chatSessions.PublishSystemMessage(
                    esc.SessionId!,
                    $"ℹ️ {agentLabel} talebinizi üstlendi ve sizinle daha sonra iletişime geçecek.");
            }

            return Results.Json(new { id, status = "acknowledged" });
        });

        app.MapPost("/escalations/{id}/resolve",
            (string id, EscalationDecisionInput? body, IEscalationPort escalations) =>
        {
            var ok = escalations.Decide(id, WellKnown.EscalationActions.Resolve,
                assignedTo: body?.AssignedTo,
                resolution: body?.Resolution);
            return ok
                ? Results.Json(new { id, status = "resolved" })
                : Results.NotFound(new { error = "Escalation bulunamadı veya zaten çözüldü." });
        });

        app.MapPost("/escalations/{id}/dismiss",
            (string id, EscalationDecisionInput? body, IEscalationPort escalations) =>
        {
            var ok = escalations.Decide(id, WellKnown.EscalationActions.Dismiss,
                assignedTo: body?.AssignedTo,
                resolution: body?.Resolution);
            return ok
                ? Results.Json(new { id, status = "dismissed" })
                : Results.NotFound(new { error = "Escalation bulunamadı veya zaten karara bağlandı." });
        });

        // Eskalasyon kartından "Yeniden Planla":
        //   - Session'a one-shot replan flag'i + opsiyonel admin notu koy
        //   - Eskalasyonu otomatik resolve et (admin müdahale etti)
        //   - Session Human modaysa Bot moduna döndür (admin sohbeti otomatik kapanır)
        //   - Müşteriye "yeniden yönlendiriliyorsunuz" sistem mesajı gönder (not İÇİNDE değil)
        //   - Arka planda son müşteri mesajını PlanningAgent'a gönder, yanıtı bridge'le müşteriye yayınla
        app.MapPost("/escalations/{id}/replan",
            async (string id,
             ReplanInput? body,
             IChatSessionPort chatSessions,
             CancellationToken ct) =>
        {
            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy)
                ? WellKnown.Defaults.Admin : body!.RequestedBy!;
            var note = string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim();
            var result = await chatSessions.ReplanEscalationAsync(id, requestedBy, note, ct);
            if (!result.Success)
            {
                return result.ErrorCode == "invalid_request"
                    ? Results.BadRequest(new { error = result.ErrorMessage })
                    : Results.NotFound(new { error = result.ErrorMessage });
            }

            return Results.Json(new
            {
                id,
                sessionId = result.SessionId,
                status = "replan_queued",
                requestedBy,
                releasedFromHuman = result.ReleasedFromHuman
            });
        });

        // Aktif sohbet panelinden "Yeniden Planla" — eskalasyon olmadan da kullanılabilir
        app.MapPost("/chat-sessions/{sid}/replan",
            async (string sid,
             ReplanInput? body,
             IChatSessionPort chatSessions,
             CancellationToken ct) =>
        {
            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy)
                ? WellKnown.Defaults.Admin : body!.RequestedBy!;
            var note = string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim();
            var result = await chatSessions.ReplanSessionAsync(sid, requestedBy, note, ct);
            if (!result.Success)
            {
                return Results.NotFound(new { error = result.ErrorMessage });
            }

            return Results.Json(new
            {
                sessionId = sid,
                status = "replan_queued",
                requestedBy,
                escalationsResolved = result.EscalationsResolved,
                releasedFromHuman = result.ReleasedFromHuman
            });
        });

        // ─── HITL LIVE TAKEOVER (chat-sessions) ───
        app.MapGet("/chat-sessions/active", (IChatSessionPort chatSessions) =>
            Results.Json(chatSessions.GetActive()));

        app.MapGet("/chat-sessions/{sid}/state", (string sid, IChatSessionPort chatSessions) =>
            Results.Json(chatSessions.GetStateOrDefault(sid)));

        app.MapGet("/chat-sessions/{sid}/history",
            (string sid, IChatSessionPort chatSessions, int take = 50) =>
                Results.Json(chatSessions.GetHistory(sid, take)));

        app.MapGet("/chat-sessions/{sid}/sentiment",
            async (string sid, IChatSessionPort chatSessions, CancellationToken ct) =>
        {
            var sentiment = await chatSessions.GetSentimentAsync(sid, ct);
            if (sentiment == null) return Results.NotFound(new { error = "Session bulunamadı." });
            return Results.Json(new
            {
                sentiment = sentiment.Sentiment,
                score = sentiment.Score,
                consecutiveNegative = sentiment.ConsecutiveNegative,
                history = sentiment.History
            });
        });

        app.MapPost("/chat-sessions/{sid}/takeover",
            (string sid,
             ChatTakeoverInput? body,
             IChatSessionPort chatSessions) =>
        {
            var agent = string.IsNullOrWhiteSpace(body?.HumanAgent) ? WellKnown.Defaults.Admin : body!.HumanAgent;
            var result = chatSessions.TakeOver(sid, agent, agent);
            if (!result.Success) return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Json(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Human,
                humanAgent = agent,
                escalationsAcknowledged = result.EscalationsAcknowledged
            });
        });

        app.MapPost("/chat-sessions/{sid}/release",
            (string sid,
             IChatSessionPort chatSessions) =>
        {
            var result = chatSessions.Release(sid);
            if (!result.Success) return Results.NotFound(new { error = result.ErrorMessage });

            return Results.Json(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Bot,
                escalationsResolved = result.EscalationsResolved
            });
        });

        app.MapPost("/chat-sessions/{sid}/messages",
            async (string sid,
             ChatAdminMessageInput? body,
             IChatSessionPort chatSessions,
             CancellationToken ct) =>
        {
            if (body == null)
                return Results.BadRequest(new { error = "text zorunlu." });

            var agent = string.IsNullOrWhiteSpace(body.HumanAgent)
                ? (chatSessions.GetStateOrDefault(sid).HumanAgent ?? WellKnown.Defaults.Admin)
                : body.HumanAgent;
            var result = await chatSessions.SendAdminMessageAsync(sid, agent, body.Text, ct);
            if (!result.Success)
                return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Json(new { sessionId = sid, ok = true });
        });

        app.MapGet("/chat-sessions/{sid}/subscribe",
            async (string sid, HttpResponse response, HttpContext httpContext,
                   IChatSessionPort chatSessions) =>
        {
            SseWriter.WriteHeaders(response);
            var ct = httpContext.RequestAborted;

            await SseWriter.WriteEventAsync(response, StreamEventTypes.Session,
                new { sessionId = sid }, ct);

            try
            {
                await foreach (var msg in chatSessions.SubscribeToAdminAsync(sid, ct))
                {
                    await SseWriter.WriteEventAsync(response, StreamEventTypes.BridgeMessage, new
                    {
                        id = msg.Id,
                        sessionId = msg.SessionId,
                        sender = msg.Sender.ToString().ToLowerInvariant(),
                        text = msg.Text,
                        humanAgent = msg.HumanAgent,
                        timestamp = msg.Timestamp
                    }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Admin sayfası kapandı
            }
        });

        // Admin panel index (tek HTML sayfası)
        app.MapGet("/admin", () => Results.Redirect("/admin.html"));

        // ─── WORKFLOW DİYAGRAMI (dokümantasyon/debug) ───
        // MAF'ın Workflow.ToMermaidString()'i — ajan takımının graph topolojisi (6 ajan +
        // GroupChatHost) tur/oturumdan bağımsız sabittir, bu yüzden parametre almaz.
        app.MapGet("/workflow/diagram", (IAgentTeamPort team) =>
            Results.Text(team.GetWorkflowDiagram(), "text/plain"));

        return app;
    }

}

