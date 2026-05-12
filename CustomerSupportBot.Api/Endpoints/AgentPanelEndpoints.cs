// Endpoints/AgentPanelEndpoints.cs
// Agent rolündeki kullanıcıların eskalasyonları yönetmesi, müşterilerle sohbet etmesi
// ve onay vermesi. Tüm endpoint'ler "AdminOrAgent" policy ile korunur.
//
// Eskalasyon:
//   GET  /agent/escalations/my              → Bana atanmış eskalasyonlar
//   GET  /agent/escalations/open            → Tüm açık eskalasyonlar
//   POST /agent/escalations/{id}/acknowledge → Üstlen (herhangi bir agent)
//   POST /agent/escalations/{id}/resolve     → Çöz
//   POST /agent/escalations/{id}/dismiss     → Reddet
//
// Onay (Approval):
//   GET  /agent/approvals/pending            → Onay bekleyen tool çağrıları
//   POST /agent/approvals/{id}/approve       → Onayla
//   POST /agent/approvals/{id}/reject        → Reddet
//
// Canlı sohbet (Live Takeover):
//   GET  /agent/chat-sessions/active              → Human modda session'lar
//   POST /agent/chat-sessions/{sid}/takeover      → Sohbete katıl
//   POST /agent/chat-sessions/{sid}/release       → Sohbeti bırak (Bot moda dön)
//   POST /agent/chat-sessions/{sid}/messages      → Müşteriye mesaj gönder
//   GET  /agent/chat-sessions/{sid}/history       → Sohbet geçmişi
//   GET  /agent/chat-sessions/{sid}/subscribe     → SSE — müşteri mesajlarını dinle
//
// Profil:
//   GET  /agent/profile                           → Kendi agent profilim

using System.Security.Claims;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services.Routing;

namespace CustomerSupportBot.Api.Endpoints;

public static class AgentPanelEndpoints
{
    public static IEndpointRouteBuilder MapAgentPanelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agent");

        // ════════════════════════════════════════════════════════════════
        // ESCALATION
        // ════════════════════════════════════════════════════════════════

        // ─── Bana atanmış eskalasyonlar ───
        group.MapGet("/escalations/my", (HttpContext ctx, IEscalationSink sink) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var mine = sink.GetOpen()
                .Where(e => string.Equals(e.SuggestedAgentId, agentId, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.AssignedTo, agentId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Results.Ok(new { agentId, count = mine.Count, items = mine });
        });

        // ─── Agent'ın görebileceği açık eskalasyonlar ───
        // Kural: atanmamış VEYA bu agent'a atanmış olanlar
        group.MapGet("/escalations/open", (HttpContext ctx, IEscalationSink sink) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var all = sink.GetOpen();
            var visible = agentId is null
                ? all
                : all.Where(e => string.IsNullOrEmpty(e.AssignedTo)
                               || string.Equals(e.AssignedTo, agentId, StringComparison.OrdinalIgnoreCase))
                     .ToList();
            return Results.Json(visible);
        });

        // ─── Üstlen (herhangi bir agent herhangi bir açık eskalasyonu üstlenebilir) ───
        group.MapPost("/escalations/{id}/acknowledge",
            (string id, HttpContext ctx, IEscalationSink sink, IHumanAgentRegistry registry,
             IChatBridge bridge) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var esc = sink.Get(id);
            if (esc is null)
                return Results.NotFound(new { error = "Escalation bulunamadı." });

            var ok = sink.Decide(id, WellKnown.EscalationActions.Acknowledge, assignedTo: agentId);
            if (!ok)
                return Results.Conflict(new { error = "Escalation zaten karara bağlanmış." });

            registry.IncrementLoad(agentId);

            // Müşteriye bildirim
            var agent = registry.Get(agentId);
            if (!string.IsNullOrEmpty(esc.SessionId))
            {
                bridge.PublishSystemMessage(esc.SessionId,
                    $"ℹ️ {agent?.DisplayName ?? agentId} talebinizi üstlendi.");
            }

            return Results.Ok(new { id, status = "acknowledged", assignedTo = agentId });
        });

        // ─── Çöz ───
        group.MapPost("/escalations/{id}/resolve",
            (string id, EscalationDecisionInput? body, HttpContext ctx,
             IEscalationSink sink, IHumanAgentRegistry registry) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var ok = sink.Decide(id, WellKnown.EscalationActions.Resolve,
                assignedTo: agentId,
                resolution: body?.Resolution);
            if (!ok)
                return Results.NotFound(new { error = "Escalation bulunamadı veya zaten çözüldü." });

            registry.DecrementLoad(agentId);

            return Results.Ok(new { id, status = "resolved", assignedTo = agentId });
        });

        // ─── Reddet ───
        group.MapPost("/escalations/{id}/dismiss",
            (string id, EscalationDecisionInput? body, HttpContext ctx,
             IEscalationSink sink) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var ok = sink.Decide(id, WellKnown.EscalationActions.Dismiss,
                assignedTo: agentId,
                resolution: body?.Resolution);
            return ok
                ? Results.Ok(new { id, status = "dismissed", assignedTo = agentId })
                : Results.NotFound(new { error = "Escalation bulunamadı veya zaten karara bağlandı." });
        });

        // ════════════════════════════════════════════════════════════════
        // APPROVALS
        // ════════════════════════════════════════════════════════════════

        group.MapGet("/approvals/pending", (IApprovalQueue queue) =>
            Results.Json(queue.GetPending()));

        group.MapPost("/approvals/{id}/approve",
            (string id, ApprovalDecisionInput? body, HttpContext ctx, IApprovalQueue queue) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var decidedBy = agentId ?? ctx.User.FindFirstValue(ClaimTypes.Name) ?? "agent";

            var req = queue.Get(id);
            if (req == null)
                return Results.NotFound(new { error = "Request bulunamadı." });

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
                decidedBy: decidedBy,
                reason: body?.Reason);
            return ok
                ? Results.Ok(new { id, status = "approved", decidedBy })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        group.MapPost("/approvals/{id}/reject",
            (string id, ApprovalDecisionInput? body, HttpContext ctx, IApprovalQueue queue) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var decidedBy = agentId ?? ctx.User.FindFirstValue(ClaimTypes.Name) ?? "agent";

            var ok = queue.Decide(id, approved: false,
                decidedBy: decidedBy,
                reason: body?.Reason ?? "Agent reddetti");
            return ok
                ? Results.Ok(new { id, status = "rejected", decidedBy })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        // ════════════════════════════════════════════════════════════════
        // LIVE TAKEOVER (sohbete katılma)
        // ════════════════════════════════════════════════════════════════

        // ─── Human modda olan session'lar ───
        group.MapGet("/chat-sessions/active", (IChatModeRegistry registry) =>
            Results.Json(registry.GetActive()));

        // ─── Sohbete katıl (takeover) ───
        group.MapPost("/chat-sessions/{sid}/takeover",
            (string sid, HttpContext ctx,
             IChatModeRegistry registry, IChatBridge bridge,
             IEscalationSink escalationSink, IHumanAgentRegistry agentRegistry) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var agentLabel = agentId is not null
                ? (agentRegistry.Get(agentId)?.DisplayName ?? agentId)
                : (ctx.User.FindFirstValue(ClaimTypes.Name) ?? "Agent");

            var ok = registry.TakeOver(sid, agentLabel);
            if (!ok) return Results.BadRequest(new { error = "TakeOver başarısız." });

            bridge.PublishSystemMessage(sid,
                $"Müşteri temsilcisi {agentLabel} sohbete katıldı.");

            // Bu session'ın açık eskalasyonlarını otomatik acknowledge yap
            var acknowledged = 0;
            foreach (var esc in escalationSink.GetOpen())
            {
                if (esc.SessionId == sid && esc.Status == EscalationStatus.Open)
                {
                    if (escalationSink.Decide(esc.Id, WellKnown.EscalationActions.Acknowledge,
                            assignedTo: agentId ?? agentLabel))
                        acknowledged++;
                }
            }

            if (agentId is not null)
                agentRegistry.IncrementLoad(agentId);

            return Results.Ok(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Human,
                humanAgent = agentLabel,
                escalationsAcknowledged = acknowledged
            });
        });

        // ─── Sohbeti bırak (release → Bot moda dön) ───
        group.MapPost("/chat-sessions/{sid}/release",
            (string sid, HttpContext ctx,
             IChatModeRegistry registry, IChatBridge bridge,
             IEscalationSink escalationSink, IHumanAgentRegistry agentRegistry) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var state = registry.GetState(sid);
            var agent = state?.HumanAgent;

            var ok = registry.Release(sid);
            if (!ok) return Results.NotFound(new { error = "Session zaten Bot modda." });

            bridge.PublishSystemMessage(sid,
                "Müşteri temsilcisi sohbeti sonlandırdı. Bot moduna dönüldü.");

            // Açık eskalasyonları resolve yap
            var resolved = 0;
            foreach (var esc in escalationSink.GetOpen())
            {
                if (esc.SessionId == sid)
                {
                    var ok2 = escalationSink.Decide(esc.Id,
                        WellKnown.EscalationActions.Resolve,
                        assignedTo: agentId ?? agent,
                        resolution: WellKnown.FallbackMessages.LiveTakeoverResolution);
                    if (ok2) resolved++;
                }
            }

            if (agentId is not null)
                agentRegistry.DecrementLoad(agentId);

            return Results.Ok(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Bot,
                escalationsResolved = resolved
            });
        });

        // ─── Müşteriye mesaj gönder ───
        group.MapPost("/chat-sessions/{sid}/messages",
            (string sid, ChatAdminMessageInput? body, HttpContext ctx,
             IChatModeRegistry registry, IChatBridge bridge, ISessionManager sessions,
             IHumanAgentRegistry agentRegistry) =>
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text zorunlu." });

            if (registry.GetMode(sid) != ChatMode.Human)
                return Results.BadRequest(new { error = "Session Human modda değil." });

            var agentId = GetLinkedAgentId(ctx);
            var agentLabel = !string.IsNullOrWhiteSpace(body.HumanAgent)
                ? body.HumanAgent
                : agentId is not null
                    ? (agentRegistry.Get(agentId)?.DisplayName ?? agentId)
                    : (registry.GetState(sid)?.HumanAgent ?? "Agent");

            var text = body.Text.Trim();
            bridge.PublishAdminMessage(sid, agentLabel, text);
            sessions.AppendAssistantMessage(sid, text);

            return Results.Ok(new { sessionId = sid, ok = true });
        });

        // ─── Sohbet geçmişi ───
        group.MapGet("/chat-sessions/{sid}/history",
            (string sid, IChatBridge bridge, int take = 50) =>
                Results.Json(bridge.GetHistory(sid, take)));

        // ─── Session sentiment (read-only) ───
        group.MapGet("/chat-sessions/{sid}/sentiment",
            (string sid, ISessionManager sessions) =>
        {
            var session = sessions.GetSession(sid);
            if (session is null) return Results.NotFound();
            var state = session.State;
            return Results.Json(new
            {
                sentiment = state.Sentiment,
                score = state.SentimentScore,
                consecutiveNegative = state.ConsecutiveNegativeTurns
            });
        });

        // ─── SSE — müşteri mesajlarını dinle ───
        group.MapGet("/chat-sessions/{sid}/subscribe",
            async (string sid, HttpResponse response, HttpContext httpContext, IChatBridge bridge) =>
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
            catch (OperationCanceledException) { }
        });

        // ════════════════════════════════════════════════════════════════
        // PROFILE
        // ════════════════════════════════════════════════════════════════

        group.MapGet("/profile", (HttpContext ctx, IHumanAgentRegistry registry) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var agent = registry.Get(agentId);
            return agent is null
                ? Results.NotFound(new { error = "Agent kaydı bulunamadı." })
                : Results.Ok(agent);
        });

        return app;
    }

    /// <summary>JWT claim'den linked_agent_id'yi çözer.</summary>
    private static string? GetLinkedAgentId(HttpContext ctx) =>
        ctx.User.FindFirstValue("linked_agent_id");
}
