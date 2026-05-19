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
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services;

namespace CustomerSupportBot.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── APPROVALS ───
        app.MapGet("/approvals/pending", (IApprovalQueue queue) =>
            Results.Json(queue.GetPending()));

        app.MapGet("/approvals/recent", (IApprovalQueue queue, int count = 50) =>
            Results.Json(queue.GetRecent(count)));

        app.MapGet("/approvals/{id}", (string id, IApprovalQueue queue) =>
        {
            var req = queue.Get(id);
            return req == null ? Results.NotFound() : Results.Json(req);
        });

        app.MapPost("/approvals/{id}/approve",
            (string id, ApprovalDecisionInput? body, IApprovalQueue queue) =>
        {
            var req = queue.Get(id);
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

            var ok = queue.Decide(id, approved: true,
                decidedBy: body?.DecidedBy ?? WellKnown.Defaults.Admin,
                reason: body?.Reason);
            return ok
                ? Results.Json(new { id, status = "approved" })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        app.MapPost("/approvals/{id}/reject",
            (string id, ApprovalDecisionInput? body, IApprovalQueue queue) =>
        {
            var ok = queue.Decide(id, approved: false,
                decidedBy: body?.DecidedBy ?? WellKnown.Defaults.Admin,
                reason: body?.Reason ?? "Admin reddetti");
            return ok
                ? Results.Json(new { id, status = "rejected" })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        // ─── ESCALATIONS ───
        app.MapGet("/escalations/open", (IEscalationSink sink) =>
            Results.Json(sink.GetOpen()));

        app.MapGet("/escalations/recent", (IEscalationSink sink, int count = 50) =>
            Results.Json(sink.GetRecent(count)));

        app.MapGet("/escalations/{id}", (string id, IEscalationSink sink) =>
        {
            var esc = sink.Get(id);
            return esc == null ? Results.NotFound() : Results.Json(esc);
        });

        app.MapPost("/escalations/{id}/acknowledge",
            (string id, EscalationDecisionInput? body, IEscalationSink sink, IChatBridge bridge) =>
        {
            var ok = sink.Decide(id, WellKnown.EscalationActions.Acknowledge,
                assignedTo: body?.AssignedTo);
            if (!ok)
            {
                return Results.NotFound(new { error = "Escalation bulunamadı veya zaten karara bağlandı." });
            }

            // Müşteriye bildirim — temsilci üstlendi, daha sonra iletişime geçilecek
            var esc = sink.Get(id);
            if (!string.IsNullOrEmpty(esc?.SessionId))
            {
                var agentLabel = string.IsNullOrWhiteSpace(body?.AssignedTo)
                    ? "Bir temsilcimiz"
                    : $"Temsilcimiz {body!.AssignedTo}";
                bridge.PublishSystemMessage(
                    esc.SessionId!,
                    $"ℹ️ {agentLabel} talebinizi üstlendi ve sizinle daha sonra iletişime geçecek.");
            }

            return Results.Json(new { id, status = "acknowledged" });
        });

        app.MapPost("/escalations/{id}/resolve",
            (string id, EscalationDecisionInput? body, IEscalationSink sink) =>
        {
            var ok = sink.Decide(id, WellKnown.EscalationActions.Resolve,
                assignedTo: body?.AssignedTo,
                resolution: body?.Resolution);
            return ok
                ? Results.Json(new { id, status = "resolved" })
                : Results.NotFound(new { error = "Escalation bulunamadı veya zaten çözüldü." });
        });

        app.MapPost("/escalations/{id}/dismiss",
            (string id, EscalationDecisionInput? body, IEscalationSink sink) =>
        {
            var ok = sink.Decide(id, WellKnown.EscalationActions.Dismiss,
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
            (string id,
             ReplanInput? body,
             IEscalationSink sink,
             ISessionManager sessions,
             IChatModeRegistry registry,
             IChatBridge bridge,
             IReplanPort replanPort) =>
        {
            var esc = sink.Get(id);
            if (esc == null) return Results.NotFound(new { error = "Escalation bulunamıyor." });
            if (string.IsNullOrEmpty(esc.SessionId))
                return Results.BadRequest(new { error = "Eskalasyona bağlı bir session yok." });

            var session = sessions.Get(esc.SessionId);
            if (session == null)
                return Results.NotFound(new { error = "Session bulunamadı." });

            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy)
                ? WellKnown.Defaults.Admin : body!.RequestedBy!;
            var note = string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim();

            // One-shot replan flag
            session.State.ForceReplanNextTurn = true;
            session.State.ReplanRequestedBy = requestedBy;
            session.State.ReplanRequestedAt = DateTime.UtcNow;
            session.State.ReplanNote = note;
            sessions.Update(session);

            // Eskalasyonu otomatik kapat — audit kaydına not düşer (varsa)
            sink.Decide(id, WellKnown.EscalationActions.Resolve,
                assignedTo: requestedBy,
                resolution: note ?? WellKnown.FallbackMessages.ReplanResolution);

            // Session Human modaysa Bot'a döndür (admin sohbeti kapansın)
            var releasedFromHuman = false;
            if (registry.GetMode(esc.SessionId) == ChatMode.Human)
                releasedFromHuman = registry.Release(esc.SessionId);

            // Müşteriye bildirim
            bridge.PublishSystemMessage(esc.SessionId, WellKnown.FallbackMessages.ReplanCustomerNotice);

            // Arka planda Application use case'i koştur
            _ = replanPort.ExecuteAsync(esc.SessionId);

            return Results.Json(new
            {
                id,
                sessionId = esc.SessionId,
                status = "replan_queued",
                requestedBy,
                releasedFromHuman
            });
        });

        // Aktif sohbet panelinden "Yeniden Planla" — eskalasyon olmadan da kullanılabilir
        app.MapPost("/chat-sessions/{sid}/replan",
            (string sid,
             ReplanInput? body,
             ISessionManager sessions,
             IEscalationSink escalationSink,
             IChatModeRegistry registry,
             IChatBridge bridge,
             IReplanPort replanPort) =>
        {
            var session = sessions.Get(sid);
            if (session == null)
                return Results.NotFound(new { error = "Session bulunamadı." });

            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy)
                ? WellKnown.Defaults.Admin : body!.RequestedBy!;
            var note = string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim();

            session.State.ForceReplanNextTurn = true;
            session.State.ReplanRequestedBy = requestedBy;
            session.State.ReplanRequestedAt = DateTime.UtcNow;
            session.State.ReplanNote = note;
            sessions.Update(session);

            // Bu session'a bağlı açık eskalasyonları da otomatik kapat
            var resolved = 0;
            foreach (var esc in escalationSink.GetOpen())
            {
                if (esc.SessionId == sid)
                {
                    var ok = escalationSink.Decide(esc.Id,
                        WellKnown.EscalationActions.Resolve,
                        assignedTo: requestedBy,
                        resolution: note ?? WellKnown.FallbackMessages.ReplanResolution);
                    if (ok) resolved++;
                }
            }

            // Session Human modaysa Bot'a döndür — admin sohbeti kapansın
            var releasedFromHuman = false;
            if (registry.GetMode(sid) == ChatMode.Human)
            {
                releasedFromHuman = registry.Release(sid);
            }

            bridge.PublishSystemMessage(sid, WellKnown.FallbackMessages.ReplanCustomerNotice);

            _ = replanPort.ExecuteAsync(sid);

            return Results.Json(new
            {
                sessionId = sid,
                status = "replan_queued",
                requestedBy,
                escalationsResolved = resolved,
                releasedFromHuman
            });
        });

        // ─── HITL LIVE TAKEOVER (chat-sessions) ───
        app.MapGet("/chat-sessions/active", (IChatModeRegistry registry) =>
            Results.Json(registry.GetActive()));

        app.MapGet("/chat-sessions/{sid}/state", (string sid, IChatModeRegistry registry) =>
        {
            var s = registry.GetState(sid);
            return Results.Json(s ?? new ChatSessionState
            {
                SessionId = sid,
                Mode = ChatMode.Bot
            });
        });

        app.MapGet("/chat-sessions/{sid}/history",
            (string sid, IChatBridge bridge, int take = 50) =>
                Results.Json(bridge.GetHistory(sid, take)));

        app.MapGet("/chat-sessions/{sid}/sentiment",
            (string sid, ISessionManager sessions) =>
        {
            var session = sessions.Get(sid);
            if (session == null) return Results.NotFound(new { error = "Session bulunamadı." });
            var state = session.State;
            return Results.Json(new
            {
                sentiment = state.Sentiment,
                score = state.SentimentScore,
                consecutiveNegative = state.ConsecutiveNegativeTurns,
                history = state.SentimentHistory.TakeLast(10).Select(e => new
                {
                    turn = e.Turn,
                    label = e.Label,
                    score = e.Score,
                    timestamp = e.Timestamp
                })
            });
        });

        app.MapPost("/chat-sessions/{sid}/takeover",
            (string sid,
             ChatTakeoverInput? body,
             IChatModeRegistry registry,
             IChatBridge bridge,
             IEscalationSink escalationSink) =>
        {
            var agent = string.IsNullOrWhiteSpace(body?.HumanAgent) ? WellKnown.Defaults.Admin : body!.HumanAgent;
            var ok = registry.TakeOver(sid, agent);
            if (!ok) return Results.BadRequest(new { error = "TakeOver başarısız." });

            // Sistem mesajı: user banner görsün
            bridge.PublishSystemMessage(sid,
                $"Müşteri temsilcisi {agent} sohbete katıldı.");

            // Bu session'ın açık eskalasyonlarını otomatik "acknowledge" yap —
            // Temsilci fiilen işi üstlendi, ayrıca "Üstlen" tıklamasına gerek yok.
            var acknowledged = 0;
            foreach (var esc in escalationSink.GetOpen())
            {
                if (esc.SessionId == sid && esc.Status == EscalationStatus.Open)
                {
                    if (escalationSink.Decide(esc.Id, WellKnown.EscalationActions.Acknowledge, assignedTo: agent))
                        acknowledged++;
                }
            }

            return Results.Json(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Human,
                humanAgent = agent,
                escalationsAcknowledged = acknowledged
            });
        });

        app.MapPost("/chat-sessions/{sid}/release",
            (string sid,
             IChatModeRegistry registry,
             IChatBridge bridge,
             IEscalationSink escalationSink) =>
        {
            var state = registry.GetState(sid);
            var agent = state?.HumanAgent;

            var ok = registry.Release(sid);
            if (!ok) return Results.NotFound(new { error = "Session zaten Bot modda." });

            bridge.PublishSystemMessage(sid,
                "Müşteri temsilcisi sohbeti sonlandırdı. Bot moduna dönüldü.");

            // Bu session'a bağlı açık/acknowledged eskalasyonları "resolve" yap —
            // Temsilci canlı sohbet üzerinden konuyu kapattı.
            var resolved = 0;
            foreach (var esc in escalationSink.GetOpen())
            {
                if (esc.SessionId == sid)
                {
                    var ok2 = escalationSink.Decide(
                        esc.Id,
                        WellKnown.EscalationActions.Resolve,
                        assignedTo: agent,
                        resolution: WellKnown.FallbackMessages.LiveTakeoverResolution);
                    if (ok2) resolved++;
                }
            }

            return Results.Json(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Bot,
                escalationsResolved = resolved
            });
        });

        app.MapPost("/chat-sessions/{sid}/messages",
            (string sid,
             ChatAdminMessageInput? body,
             IChatModeRegistry registry,
             IChatBridge bridge,
             ISessionManager sessions) =>
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text zorunlu." });

            if (registry.GetMode(sid) != ChatMode.Human)
                return Results.BadRequest(new { error = "Session Human modda değil." });

            var agent = string.IsNullOrWhiteSpace(body.HumanAgent)
                ? (registry.GetState(sid)?.HumanAgent ?? WellKnown.Defaults.Admin)
                : body.HumanAgent;

            var text = body.Text.Trim();
            bridge.PublishAdminMessage(sid, agent, text);

            // Admin yanıtını session history'sine de yaz — böylece bot Release/Replan
            // sonrası PlanningAgent admin'in verdiği vaatleri / yönlendirmeleri görür.
            sessions.AppendAssistantMessage(sid, text);

            return Results.Json(new { sessionId = sid, ok = true });
        });

        app.MapGet("/chat-sessions/{sid}/subscribe",
            async (string sid, HttpResponse response, HttpContext httpContext,
                   IChatBridge bridge) =>
        {
            SseWriter.WriteHeaders(response);
            var ct = httpContext.RequestAborted;

            await SseWriter.WriteEventAsync(response, StreamEventTypes.Session,
                new { sessionId = sid }, ct);

            try
            {
                await foreach (var msg in bridge.SubscribeToAdminAsync(sid, ct))
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

        return app;
    }

}


