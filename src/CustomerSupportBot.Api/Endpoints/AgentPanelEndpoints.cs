// Endpoints/AgentPanelEndpoints.cs
// Agent rolündeki kullanıcıların eskalasyonları yönetmesi, müşterilerle sohbet etmesi
// ve onay vermesi. Tüm endpoint'ler "AdminOrAgent" policy ile korunur.
//
// Eskalasyon:
//   GET  /agent/escalations/my              → Bana atanmış eskalasyonlar
//   GET  /agent/escalations/open            → Tüm açık eskalasyonlar
//   GET  /agent/escalations/recent          → Son N karar (geçmiş sekmesi)
//   POST /agent/escalations/{id}/acknowledge → Üstlen (herhangi bir agent)
//   POST /agent/escalations/{id}/resolve     → Çöz
//   POST /agent/escalations/{id}/dismiss     → Reddet
//
// Onay:
//   GET  /agent/approvals/pending           → Bekleyen onaylar
//   GET  /agent/approvals/recent            → Son N karar (geçmiş sekmesi)
//   GET  /agent/approvals/stuck             → Yürütmesi askıda kalmış onaylar
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
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

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
        group.MapGet("/escalations/my", (HttpContext ctx, IEscalationPort escalations) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var mine = escalations.GetOpen()
                .Where(e => string.Equals(e.SuggestedAgentId, agentId, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.AssignedTo, agentId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Results.Ok(new { agentId, count = mine.Count, items = mine });
        });

        // ─── Geçmiş: kapanmış eskalasyonlar ───
        // AYNI kapsam kuralı burada da geçerlidir: atanmamış VEYA bu agent'a atanmış olanlar.
        // Filtresiz bırakılırsa her agent, diğer agent'ların kapattığı eskalasyonların müşteri
        // sorularını ve çözüm notlarını okur — açık kayıtlarda uygulanan kısıtı geçmiş üzerinden
        // dolaşmak olurdu.
        group.MapGet("/escalations/recent", async (
            HttpContext ctx, IEscalationPort escalations, CancellationToken ct, int count = 50) =>
        {
            if (!TryResolveEscalationScope(ctx, out var agentScope, out var denied)) return denied!;

            // Daraltma ve limit birlikte, veri kaynağında (bkz. IEscalationPort.GetRecentForAgentAsync).
            return Results.Json(agentScope is null
                ? escalations.GetRecent(count)
                : await escalations.GetRecentForAgentAsync(agentScope, count, ct));
        });

        // ─── Agent'ın görebileceği açık eskalasyonlar ───
        // Kural: atanmamış VEYA bu agent'a atanmış olanlar
        group.MapGet("/escalations/open", (HttpContext ctx, IEscalationPort escalations) =>
        {
            if (!TryResolveEscalationScope(ctx, out var agentScope, out var denied)) return denied!;

            var all = escalations.GetOpen();
            var visible = agentScope is null
                ? all
                : all.Where(e => string.IsNullOrEmpty(e.AssignedTo)
                               || string.Equals(e.AssignedTo, agentScope, StringComparison.OrdinalIgnoreCase))
                     .ToList();
            return Results.Json(visible);
        });

        // ─── Üstlen (herhangi bir agent herhangi bir açık eskalasyonu üstlenebilir) ───
        group.MapPost("/escalations/{id}/acknowledge",
            (string id, HttpContext ctx, IEscalationPort escalations, IHumanAgentPort agents,
             IChatSessionPort chatSessions) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var esc = escalations.Get(id);
            if (esc is null)
                return Results.NotFound(new { error = "Escalation bulunamadı." });

            var ok = escalations.Decide(id, WellKnown.EscalationActions.Acknowledge, assignedTo: agentId);
            if (!ok)
                return Results.Conflict(new { error = "Escalation zaten karara bağlanmış." });

            agents.IncrementLoad(agentId);

            // Müşteriye bildirim
            var agent = agents.GetAgent(agentId);
            if (!string.IsNullOrEmpty(esc.SessionId))
            {
                chatSessions.PublishSystemMessage(esc.SessionId,
                    $"ℹ️ {agent?.DisplayName ?? agentId} talebinizi üstlendi.");
            }

            return Results.Ok(new { id, status = "acknowledged", assignedTo = agentId });
        });

        // ─── Çöz ───
        group.MapPost("/escalations/{id}/resolve",
            (string id, EscalationDecisionInput? body, HttpContext ctx,
             IEscalationPort escalations, IHumanAgentPort agents) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var ok = escalations.Decide(id, WellKnown.EscalationActions.Resolve,
                assignedTo: agentId,
                resolution: body?.Resolution);
            if (!ok)
                return Results.NotFound(new { error = "Escalation bulunamadı veya zaten çözüldü." });

            agents.DecrementLoad(agentId);

            return Results.Ok(new { id, status = "resolved", assignedTo = agentId });
        });

        // ─── Yeniden Planla ───
        group.MapPost("/escalations/{id}/replan",
            async (string id,
             ReplanInput? body,
             HttpContext ctx,
             IChatSessionPort chatSessions,
             CancellationToken ct) =>
        {
            var agentId = GetLinkedAgentId(ctx)
                ?? ctx.User.FindFirstValue(ClaimTypes.Name)
                ?? WellKnown.Defaults.Admin;
            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy) ? agentId : body!.RequestedBy!;
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

        // ─── Reddet ───
        group.MapPost("/escalations/{id}/dismiss",
            (string id, EscalationDecisionInput? body, HttpContext ctx,
             IEscalationPort escalations) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var ok = escalations.Decide(id, WellKnown.EscalationActions.Dismiss,
                assignedTo: agentId,
                resolution: body?.Resolution);
            return ok
                ? Results.Ok(new { id, status = "dismissed", assignedTo = agentId })
                : Results.NotFound(new { error = "Escalation bulunamadı veya zaten karara bağlandı." });
        });

        // ════════════════════════════════════════════════════════════════
        // APPROVALS
        // ════════════════════════════════════════════════════════════════

        group.MapGet("/approvals/pending", async (IApprovalPort approvals, CancellationToken ct) =>
            Results.Json(await approvals.GetPendingAsync(ct)));

        // Panelin "geçmiş" sekmesi bunu çağırır (AdminApiService rolü Agent ise /agent önekini
        // ekler). Karşılığı tanımlı olmadığı için agent kullanıcıları sekmeye geçtiğinde 404
        // alıyordu — admin'de çalışan bir ekran agent'ta sessizce boş kalıyordu.
        group.MapGet("/approvals/recent", async (IApprovalPort approvals, CancellationToken ct, int count = 50) =>
            Results.Json(await approvals.GetRecentAsync(count, ct)));

        group.MapGet("/approvals/stuck", async (IApprovalPort approvals, CancellationToken ct) =>
            Results.Json(await approvals.GetStuckExecutionsAsync(ct)));

        group.MapPost("/approvals/{id}/approve",
            async (string id, ApprovalDecisionInput? body, HttpContext ctx, IApprovalPort approvals, CancellationToken ct) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var decidedBy = agentId ?? ctx.User.FindFirstValue(ClaimTypes.Name) ?? "agent";

            var req = await approvals.GetAsync(id, ct);
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

            var ok = await approvals.DecideAsync(id, approved: true,
                decidedBy: decidedBy,
                reason: body?.Reason, ct: ct);
            return ok
                ? Results.Ok(new { id, status = "approved", decidedBy })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        group.MapPost("/approvals/{id}/reject",
            async (string id, ApprovalDecisionInput? body, HttpContext ctx, IApprovalPort approvals, CancellationToken ct) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var decidedBy = agentId ?? ctx.User.FindFirstValue(ClaimTypes.Name) ?? "agent";

            var ok = await approvals.DecideAsync(id, approved: false,
                decidedBy: decidedBy,
                reason: body?.Reason ?? "Agent reddetti", ct: ct);
            return ok
                ? Results.Ok(new { id, status = "rejected", decidedBy })
                : Results.NotFound(new { error = "Request bulunamadı veya zaten karara bağlandı." });
        });

        // ════════════════════════════════════════════════════════════════
        // LIVE TAKEOVER (sohbete katılma)
        // ════════════════════════════════════════════════════════════════

        // ─── Human modda olan session'lar ───
        group.MapGet("/chat-sessions/active", (IChatSessionPort chatSessions) =>
            Results.Json(chatSessions.GetActive()));

        // ─── Sohbete katıl (takeover) ───
        group.MapPost("/chat-sessions/{sid}/takeover",
            (string sid, HttpContext ctx,
             IChatSessionPort chatSessions, IHumanAgentPort agents) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var agentLabel = agentId is not null
                ? (agents.GetAgent(agentId)?.DisplayName ?? agentId)
                : (ctx.User.FindFirstValue(ClaimTypes.Name) ?? "Agent");

            var result = chatSessions.TakeOver(sid, agentLabel, agentId);
            if (!result.Success) return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Ok(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Human,
                humanAgent = agentLabel,
                escalationsAcknowledged = result.EscalationsAcknowledged
            });
        });

        // ─── Sohbeti bırak (release → Bot moda dön) ───
        group.MapPost("/chat-sessions/{sid}/release",
            (string sid, HttpContext ctx,
             IChatSessionPort chatSessions) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            var result = chatSessions.Release(sid, agentId);
            if (!result.Success) return Results.NotFound(new { error = result.ErrorMessage });

            return Results.Ok(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Bot,
                escalationsResolved = result.EscalationsResolved
            });
        });

        // ─── Müşteriye mesaj gönder ───
        group.MapPost("/chat-sessions/{sid}/messages",
            async (string sid, ChatAdminMessageInput? body, HttpContext ctx,
             IChatSessionPort chatSessions, IHumanAgentPort agents, CancellationToken ct) =>
        {
            if (body == null)
                return Results.BadRequest(new { error = "text zorunlu." });

            var agentId = GetLinkedAgentId(ctx);
            var agentLabel = !string.IsNullOrWhiteSpace(body.HumanAgent)
                ? body.HumanAgent
                : agentId is not null
                    ? (agents.GetAgent(agentId)?.DisplayName ?? agentId)
                    : (chatSessions.GetStateOrDefault(sid).HumanAgent ?? "Agent");
            var result = await chatSessions.SendAdminMessageAsync(sid, agentLabel, body.Text, ct);
            if (!result.Success)
                return Results.BadRequest(new { error = result.ErrorMessage });

            return Results.Ok(new { sessionId = sid, ok = true });
        });

        // ─── Sohbet geçmişi ───
        group.MapGet("/chat-sessions/{sid}/history",
            (string sid, IChatSessionPort chatSessions, int take = 50) =>
                Results.Json(chatSessions.GetHistory(sid, take)));

        // ─── Session sentiment (read-only) ───
        group.MapGet("/chat-sessions/{sid}/sentiment",
            async (string sid, IChatSessionPort chatSessions, CancellationToken ct) =>
        {
            var sentiment = await chatSessions.GetSentimentAsync(sid, ct);
            if (sentiment is null) return Results.NotFound();
            return Results.Json(new
            {
                sentiment = sentiment.Sentiment,
                score = sentiment.Score,
                consecutiveNegative = sentiment.ConsecutiveNegative
            });
        });

        // ─── Yeniden Planla (chat-sessions) ───
        group.MapPost("/chat-sessions/{sid}/replan",
            async (string sid,
             ReplanInput? body,
             HttpContext ctx,
             IChatSessionPort chatSessions,
             CancellationToken ct) =>
        {
            var agentId = GetLinkedAgentId(ctx)
                ?? ctx.User.FindFirstValue(ClaimTypes.Name)
                ?? WellKnown.Defaults.Admin;
            var requestedBy = string.IsNullOrWhiteSpace(body?.RequestedBy) ? agentId : body!.RequestedBy!;
            var note = string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim();
            var result = await chatSessions.ReplanSessionAsync(sid, requestedBy, note, ct);
            if (!result.Success)
                return Results.NotFound(new { error = result.ErrorMessage });

            return Results.Json(new
            {
                sessionId = sid,
                status = "replan_queued",
                requestedBy,
                escalationsResolved = result.EscalationsResolved,
                releasedFromHuman = result.ReleasedFromHuman
            });
        });

        // ─── SSE — müşteri mesajlarını dinle ───
        group.MapGet("/chat-sessions/{sid}/subscribe",
            async (string sid, HttpResponse response, HttpContext httpContext, IChatSessionPort chatSessions) =>
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
            catch (OperationCanceledException) { }
        });

        // ════════════════════════════════════════════════════════════════
        // PROFILE
        // ════════════════════════════════════════════════════════════════

        group.MapGet("/profile", (HttpContext ctx, IHumanAgentPort agents) =>
        {
            var agentId = GetLinkedAgentId(ctx);
            if (agentId is null)
                return Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });

            var agent = agents.GetAgent(agentId);
            return agent is null
                ? Results.NotFound(new { error = "Agent kaydı bulunamadı." })
                : Results.Ok(agent);
        });

        return app;
    }

    /// <summary>JWT claim'den linked_agent_id'yi çözer.</summary>
    private static string? GetLinkedAgentId(HttpContext ctx) =>
        ctx.User.FindFirstValue("linked_agent_id");

    /// <summary>
    /// Liste uçlarının kapsamı: <c>null</c> = sınırsız (tüm kayıtlar), aksi hâlde yalnızca o
    /// agent'ın görebilecekleri. <c>false</c> dönerse çağıranın kapsamı yoktur.
    ///
    /// <para>
    /// Sınırsız erişim <b>Admin rolünden</b> türetilir, <c>linked_agent_id</c> claim'inin
    /// yokluğundan DEĞİL. Bu grup "AdminOrAgent" ile korunur ve <c>LinkedAgentId</c> veritabanında
    /// nullable'dır; "claim yoksa hepsini göster" kuralı, bağlantısı kurulmamış bir Agent hesabını
    /// sessizce Admin kapsamına yükseltirdi. Eylem uçları (acknowledge/resolve/dismiss) zaten
    /// claim yoksa 400 döner — liste uçlarının onlardan daha geniş olması tutarsızlıktı.
    /// </para>
    /// </summary>
    private static bool TryResolveEscalationScope(
        HttpContext ctx, out string? agentScope, out IResult? denied)
    {
        agentScope = null;
        denied = null;

        if (ctx.User.IsInRole("Admin")) return true;

        var agentId = GetLinkedAgentId(ctx);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            denied = Results.BadRequest(new { error = "Kullanıcıya bağlı agent kaydı yok." });
            return false;
        }

        agentScope = agentId;
        return true;
    }
}
