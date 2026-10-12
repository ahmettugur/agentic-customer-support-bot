// Endpoints/ComplaintEndpoints.cs
// Şikayet yönetimi. Admin: /complaints (adminScope). Temsilci: /agent/complaints (agentScope, AdminOrAgent).
// Kural ve yetki ComplaintManagementService + ComplaintTransitions'ta; burada aktör çıkarımı ve HTTP eşlemesi.

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class ComplaintEndpoints
{
    public sealed record VersionInput(int? ExpectedVersion);
    public sealed record AssignInput(string? AgentId, int? ExpectedVersion);
    public sealed record TextInput(string? Text, string? Note, int? ExpectedVersion);

    private const string NotLinked = "Hesabınız bir temsilci kaydına bağlı değil.";

    public static IEndpointRouteBuilder MapAdminComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/complaints", async (string? status, string? assigned, int? page, int? pageSize, HttpContext ctx,
            IComplaintManagementPort port, IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
        {
            var query = new ComplaintQuery(
                Status: string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                AssignedAgentId: assigned is null or "unassigned" ? null : assigned,
                UnassignedOnly: assigned == "unassigned",
                Page: page ?? 1, PageSize: pageSize ?? 20);
            return Results.Ok(PageView(await port.ListAsync(query, ct), query, ActorOf(ctx)!, agents, sla.CurrentValue));
        });

        app.MapGet("/complaints/counts", async (IComplaintManagementPort port, CancellationToken ct) =>
            Results.Ok(CountsView(await port.CountsAsync(null, ct))));

        app.MapGet("/complaints/{code}", (string code, HttpContext ctx, IComplaintManagementPort port, IHumanAgentRegistry agents,
            IOptionsMonitor<SlaOptions> sla, CancellationToken ct) => Detail(code, ActorOf(ctx)!, port, agents, sla, ct));

        app.MapPost("/complaints/{code}/assign", (string code, AssignInput? body, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            Run(code, ctx, port, agents, sla, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: body?.AgentId,
                ExpectedVersion: body?.ExpectedVersion), ct));

        MapCommonActions(app, "/complaints");
        return app;
    }

    public static IEndpointRouteBuilder MapAgentComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/complaints", async (string? status, bool? mine, int? page, int? pageSize, HttpContext ctx,
            IComplaintManagementPort port, IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
        {
            var actor = ActorOf(ctx);
            if (actor is null) return Results.BadRequest(new { error = NotLinked });
            var me = actor.IsAdmin ? null : actor.AgentId;
            var query = new ComplaintQuery(
                Status: string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                AssignedAgentId: mine == true ? me : null,
                VisibleToAgentId: me,
                Page: page ?? 1, PageSize: pageSize ?? 20);
            return Results.Ok(PageView(await port.ListAsync(query, ct), query, actor, agents, sla.CurrentValue));
        });

        app.MapGet("/agent/complaints/counts", async (HttpContext ctx, IComplaintManagementPort port, CancellationToken ct) =>
        {
            var actor = ActorOf(ctx);
            if (actor is null) return Results.BadRequest(new { error = NotLinked });
            return Results.Ok(CountsView(await port.CountsAsync(actor.AgentId, ct)));
        });

        app.MapGet("/agent/complaints/{code}", (string code, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            ActorOf(ctx) is { } actor ? Detail(code, actor, port, agents, sla, ct) : Task.FromResult(Results.BadRequest(new { error = NotLinked })));

        app.MapPost("/agent/complaints/{code}/claim", (string code, VersionInput? body, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            Run(code, ctx, port, agents, sla, new ComplaintCommand(ComplaintAction.Claim, ExpectedVersion: body?.ExpectedVersion), ct));

        MapCommonActions(app, "/agent/complaints");
        return app;
    }

    private static void MapCommonActions(IEndpointRouteBuilder app, string prefix)
    {
        foreach (var (segment, action) in new[]
                 {
                     ("resolve", ComplaintAction.Resolve), ("reject", ComplaintAction.Reject),
                     ("reopen", ComplaintAction.Reopen), ("notes", ComplaintAction.Note)
                 })
        {
            var a = action;
            app.MapPost($"{prefix}/{{code}}/{segment}", (string code, TextInput? body, HttpContext ctx, IComplaintManagementPort port,
                IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
                Run(code, ctx, port, agents, sla, new ComplaintCommand(a, Text: body?.Text, InternalNote: body?.Note,
                    ExpectedVersion: body?.ExpectedVersion), ct));
        }
    }

    private static async Task<IResult> Run(string code, HttpContext ctx, IComplaintManagementPort port, IHumanAgentRegistry agents,
        IOptionsMonitor<SlaOptions> sla, ComplaintCommand command, CancellationToken ct)
    {
        var actor = ActorOf(ctx);
        if (actor is null) return Results.BadRequest(new { error = NotLinked });
        var r = await port.ExecuteAsync(code, actor, command, ct);
        return r.Status switch
        {
            ComplaintCommandStatus.Updated => Results.Ok(View(code, r.Complaint!, actor, agents, sla.CurrentValue)),
            ComplaintCommandStatus.NotFound => Results.NotFound(new { error = "Şikayet bulunamadı." }),
            ComplaintCommandStatus.Forbidden => Results.Json(new { error = r.Error }, statusCode: StatusCodes.Status403Forbidden),
            ComplaintCommandStatus.Invalid => Results.BadRequest(new { error = r.Error }),
            _ => Results.Conflict(new { error = r.Error, currentStatus = r.Complaint?.Status, version = r.Complaint?.Version })
        };
    }

    private static async Task<IResult> Detail(string code, ComplaintActor actor, IComplaintManagementPort port,
        IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct)
    {
        var d = await port.GetAsync(code, ct);
        if (d is null) return Results.NotFound(new { error = "Şikayet bulunamadı." });
        return Results.Ok(new
        {
            complaint = View(code, d.Complaint, actor, agents, sla.CurrentValue),
            order = d.Order is null ? null : new { status = d.Order.Status, linesSummary = d.Order.LinesSummary() },
            attachmentIds = d.AttachmentIds,
            events = d.Events.Select(e => new
            {
                at = e.At, actor = e.Actor, kind = e.Kind, fromStatus = e.FromStatus, toStatus = e.ToStatus,
                assignedAgentId = e.AssignedAgentId, note = e.Note
            })
        });
    }

    private static object PageView(ComplaintPage page, ComplaintQuery query, ComplaintActor actor, IHumanAgentRegistry agents, SlaOptions sla) => new
    {
        items = page.Items.Select(i => View(i.ComplaintId, i.Complaint, actor, agents, sla)),
        total = page.Total,
        page = Math.Max(query.Page, 1),
        pageSize = Math.Clamp(query.PageSize, 1, 500)
    };

    private static object CountsView(ComplaintCounts c) => new { pending = c.Pending, inProgress = c.InProgress, assignedToMe = c.AssignedToAgent };

    private static object View(string id, ComplaintInfo c, ComplaintActor actor, IHumanAgentRegistry agents, SlaOptions sla) => new
    {
        complaintId = id,
        orderId = c.OrderId,
        customerId = c.CustomerId,
        complaint = c.Complaint,
        status = c.Status,
        assignedAgentId = c.AssignedAgentId,
        assignedAgentName = c.AssignedAgentId is { } a ? agents.Get(a)?.DisplayName : null,
        resolution = c.Resolution,
        createdAt = c.CreatedAt,
        updatedAt = c.UpdatedAt,
        version = c.Version,
        sla = SlaPolicyEvaluator.ComplaintTone(c, sla.Complaints, DateTime.UtcNow),
        actions = ComplaintTransitions.AvailableActions(c, actor).Select(ComplaintTransitions.ActionName)
    };

    /// <summary>Admin rolü → admin aktör; değilse <c>linked_agent_id</c> claim'i; ikisi de yoksa null.</summary>
    private static ComplaintActor? ActorOf(HttpContext ctx)
    {
        var name = ctx.User.FindFirstValue(ClaimTypes.Name) ?? "personel";
        if (ctx.User.IsInRole("Admin")) return new ComplaintActor(ComplaintActorRole.Admin, null, name);
        var agentId = ctx.User.FindFirstValue("linked_agent_id");
        return string.IsNullOrWhiteSpace(agentId) ? null : new ComplaintActor(ComplaintActorRole.Agent, agentId, name);
    }
}
