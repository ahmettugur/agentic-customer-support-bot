// Temsilci–müşteri sesli görüşmesi. Personel uçları "AdminOrAgent" grubunda (Program.cs), müşteri uçları
// "Customer" politikasıyla. Ses bu uçlardan geçmez — yalnızca sinyal ve kayıt parçaları.

using System.Security.Claims;
using System.Text;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportBot.Api.Endpoints;

public static class VoiceCallEndpoints
{
    private const long MaxChunkRequestBytes = 3 * 1024 * 1024;
    private const long MaxSignalBytes = 64 * 1024;

    /// <summary>Personel uçları — AdminOrAgent grubuna eşlenir.</summary>
    public static IEndpointRouteBuilder MapStaffVoiceCallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat-sessions/{sid}/voice-calls", async (string sid, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.StartAsync(sid, Staff(ctx), ct);
            return r.Ok ? Results.Created($"/voice-calls/{r.Call!.Id}", Dto(r.Call)) : Error(r.Error!.Value);
        });

        app.MapGet("/chat-sessions/{sid}/voice-calls", async (string sid, IVoiceCallPort port, CancellationToken ct) =>
            Results.Ok((await port.ListForSessionAsync(sid, ct)).Select(Dto)));

        app.MapGet("/voice-calls/mine", async (HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
            await port.GetOpenForStaffAsync(Staff(ctx), ct) is { } call ? Results.Ok(Dto(call)) : Results.NoContent());

        app.MapPost("/voice-calls/{id}/signal", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var payload = await ReadBodyAsync(ctx.Request, MaxSignalBytes, ct);
            if (payload is null) return Error(VoiceCallError.Invalid);
            var r = await port.SignalFromStaffAsync(id, Staff(ctx), payload, ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        });

        app.MapPost("/voice-calls/{id}/hangup", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.HangupByStaffAsync(id, Staff(ctx), reason ?? "", ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        app.MapPost("/voice-calls/{id}/chunks", async (string id, string track, int seq, int offsetMs, int durationMs,
            HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            if (!Enum.TryParse<VoiceTrack>(track, ignoreCase: true, out var t)) return Error(VoiceCallError.Invalid);
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ct);
            var contentType = ctx.Request.ContentType ?? "audio/webm";
            if (!contentType.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith("audio/ogg", StringComparison.OrdinalIgnoreCase))
                return Error(VoiceCallError.Invalid);
            var r = await port.UploadChunkAsync(id, Staff(ctx), t, seq, offsetMs, durationMs, contentType, ms.ToArray(), ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxChunkRequestBytes));

        app.MapGet("/voice-calls/{id}", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (view, error) = await port.GetViewAsync(id, Staff(ctx), ct);
            return view is null ? Error(error!.Value) : Results.Ok(new
            {
                call = Dto(view.Call),
                lines = view.Lines.Select(l => new { chunkId = l.ChunkId, track = l.Track, offsetMs = l.OffsetMs, text = l.Text, status = l.Status.ToLowerInvariant() })
            });
        });

        app.MapGet("/voice-calls/{id}/chunks/{chunkId}", async (string id, string chunkId, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (chunk, error) = await port.GetChunkAudioAsync(id, chunkId, Staff(ctx), ct);
            return chunk is null ? Error(error!.Value) : Results.File(chunk.Data, chunk.ContentType);
        });

        app.MapGet("/voice-calls/{id}/ice-config", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (config, error) = await port.GetIceConfigForStaffAsync(id, Staff(ctx), ct);
            return config is null ? Error(error!.Value) : Results.Ok(IceDto(config));
        });

        return app;
    }

    /// <summary>Müşteri uçları.</summary>
    public static IEndpointRouteBuilder MapCustomerVoiceCallEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/chat/voice-calls").RequireAuthorization("Customer").RequireRateLimiting("general");

        g.MapPost("/{id}/accept", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.AcceptAsync(id, CustomerId(ctx), ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/decline", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.DeclineAsync(id, CustomerId(ctx), reason ?? VoiceCallEndReasons.Declined, ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/signal", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var payload = await ReadBodyAsync(ctx.Request, MaxSignalBytes, ct);
            if (payload is null) return Error(VoiceCallError.Invalid);
            var r = await port.SignalFromCustomerAsync(id, CustomerId(ctx), payload, ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/hangup", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.HangupByCustomerAsync(id, CustomerId(ctx), reason ?? "", ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapGet("/{id}/ice-config", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (config, error) = await port.GetIceConfigForCustomerAsync(id, CustomerId(ctx), ct);
            return config is null ? Error(error!.Value) : Results.Ok(IceDto(config));
        });

        return app;
    }

    // ── yardımcılar ───────────────────────────────────────────────────────────

    /// <summary>
    /// Temsilci kimliği: Agent rolünde bağlı kayıt (<c>linked_agent_id</c>); yönetici için kullanıcı kimliği
    /// (<c>user:{id}</c>) — yöneticinin de aynı anda tek görüşmesi olur.
    /// </summary>
    private static StaffCaller Staff(HttpContext ctx)
    {
        var u = ctx.User;
        var isAdmin = u.IsInRole("Admin");
        var linked = u.FindFirstValue("linked_agent_id");
        var agentId = !string.IsNullOrEmpty(linked) ? linked : $"user:{u.FindFirstValue(ClaimTypes.NameIdentifier)}";
        // Görünen ad: bağlı temsilci kaydı (AgentPanelEndpoints'teki agentLabel deseni), yoksa token'daki ad.
        var registered = !string.IsNullOrEmpty(linked)
            ? ctx.RequestServices.GetService<IHumanAgentPort>()?.GetAgent(linked)?.DisplayName
            : null;
        var name = registered ?? u.FindFirstValue(ClaimTypes.GivenName) ?? u.FindFirstValue(ClaimTypes.Name) ?? "Temsilci";
        return new StaffCaller(agentId, name, isAdmin);
    }

    private static string? CustomerId(HttpContext ctx) => ctx.User.FindFirst("linked_customer_id")?.Value;

    private static async Task<string?> ReadBodyAsync(HttpRequest request, long max, CancellationToken ct)
    {
        if (request.ContentLength > max) return null;
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);
        return text.Length == 0 || text.Length > max ? null : text;
    }

    private static object Dto(VoiceCall c) => new
    {
        id = c.Id,
        sessionId = c.SessionId,
        agentDisplayName = c.AgentDisplayName,
        status = c.Status.ToString().ToLowerInvariant(),
        createdAt = c.CreatedAt,
        answeredAt = c.AnsweredAt,
        endedAt = c.EndedAt,
        endReason = c.EndReason,
        durationSeconds = c.Duration is { } d ? (int?)d.TotalSeconds : null
    };

    private static object IceDto(IceServerConfig c) => new
    {
        iceServers = c.IceServers.Select(s => new { urls = s.Urls, username = s.Username, credential = s.Credential })
    };

    private static IResult Error(VoiceCallError error) => error switch
    {
        VoiceCallError.Busy => Results.Conflict(new { error = "voice_call_busy" }),
        VoiceCallError.NotInHumanMode => Results.Conflict(new { error = "not_in_human_mode" }),
        VoiceCallError.InvalidState => Results.Conflict(new { error = "invalid_state" }),
        VoiceCallError.Forbidden => Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden),
        VoiceCallError.NotFound => Results.NotFound(new { error = "not_found" }),
        VoiceCallError.TooLarge => Results.Json(new { error = "too_large" }, statusCode: StatusCodes.Status413PayloadTooLarge),
        VoiceCallError.Disabled => Results.Json(new { error = "disabled" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.BadRequest(new { error = "invalid" })
    };
}
