// Endpoints/ConversationClosingEndpoints.cs
// Canlı sohbeti kapanış nedeni, etiketler ve notla kapatma.
//   GET  /conversation-closing/options          [Admin]   GET  /agent/conversation-closing/options     [AdminOrAgent]
//   POST /chat-sessions/{sid}/close             [Admin]   POST /agent/chat-sessions/{sid}/close        [AdminOrAgent]
// Mevcut …/release uçları değişmez (kapanış kaydı oluşturmaz). Yetki ve hız sınırı çağıran grupta
// (Program.cs: adminScope / agentScope).

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class ConversationClosingEndpoints
{
    public static IEndpointRouteBuilder MapAdminConversationClosingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/conversation-closing/options", GetOptionsAsync);
        // Yönetici bir temsilci kaydına bağlı değildir — yük düşürülmez (mevcut release ucuyla aynı).
        app.MapPost("/chat-sessions/{sid}/close",
            (string sid, ConversationClosingInput? body, HttpContext http, IConversationClosingPort closing, CancellationToken ct) =>
                CloseAsync(sid, body, http, agentId: null, closing, ct));
        return app;
    }

    public static IEndpointRouteBuilder MapAgentConversationClosingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/conversation-closing/options", GetOptionsAsync);
        app.MapPost("/agent/chat-sessions/{sid}/close",
            (string sid, ConversationClosingInput? body, HttpContext http, IConversationClosingPort closing, CancellationToken ct) =>
                CloseAsync(sid, body, http, http.User.FindFirstValue("linked_agent_id"), closing, ct));
        return app;
    }

    private static async Task<IResult> GetOptionsAsync(IConversationClosingPort closing, CancellationToken ct) =>
        Results.Ok(await closing.GetOptionsAsync(ct));

    private static async Task<IResult> CloseAsync(
        string sid, ConversationClosingInput? body, HttpContext http, string? agentId, IConversationClosingPort closing, CancellationToken ct)
    {
        var result = await closing.CloseAsync(
            sid, body ?? new ConversationClosingInput(null, null, null), http.User.FindFirstValue(ClaimTypes.Name), agentId, ct);
        return result.Status switch
        {
            ConversationClosingStatus.Invalid => Results.BadRequest(new { error = result.Error }),
            ConversationClosingStatus.NotLive => Results.NotFound(new { error = result.Error }),
            _ => Results.Ok(new
            {
                sessionId = sid,
                mode = WellKnown.ChatModes.Bot,
                escalationsResolved = result.EscalationsResolved,
                disposition = result.Disposition,
                // Sohbet kapandı ama kayıt yazılamadıysa panel uyarı gösterir.
                warning = result.Error
            })
        };
    }
}
