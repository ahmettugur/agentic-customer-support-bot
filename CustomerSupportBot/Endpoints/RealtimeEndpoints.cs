// Endpoints/RealtimeEndpoints.cs
// Sesli sohbet için WebSocket endpoint'i.
// Browser bu endpoint'e bağlanır; backend RealtimeBridge ile OpenAI Realtime API'ye köprü kurar.

using CustomerSupportBot.Services.Realtime;

namespace CustomerSupportBot.Endpoints;

public static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        // ws://host/chat/realtime/{sessionId?}
        app.Map("/chat/realtime/{sessionId?}", HandleRealtimeAsync);
        return app;
    }

    private static async Task HandleRealtimeAsync(
        HttpContext httpContext,
        string? sessionId,
        RealtimeBridge bridge,
        ILogger<RealtimeBridge> logger)
    {
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket bağlantısı bekleniyor.");
            return;
        }

        var ws = await httpContext.WebSockets.AcceptWebSocketAsync();
        var sid = sessionId ?? Guid.NewGuid().ToString();

        try
        {
            await bridge.RunAsync(ws, sid, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) { /* client kapattı */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Realtime endpoint hata session={Sid}", sid);
        }
        finally
        {
            if (ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        "session_end",
                        CancellationToken.None);
                }
                catch { /* best effort */ }
            }
        }
    }
}
