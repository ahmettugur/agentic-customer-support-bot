// Endpoints/RealtimeEndpoints.cs
// Sesli sohbet için WebSocket endpoint'i.
// Browser bu endpoint'e bağlanır; backend RealtimeBridge ile OpenAI Realtime API'ye köprü kurar.

using CustomerSupportBot.Adapters.AI.Realtime;

namespace CustomerSupportBot.Api.Endpoints;

public static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        // Köprü modu — gpt-realtime-1.5 sadece STT/TTS, agent pipeline cevabı üretir.
        // ws://host/chat/realtime/{sessionId?}
        app.Map("/chat/realtime/{sessionId?}", HandleRealtimeAsync);

        // Native mod — gpt-realtime-1.5 kendisi konuşur, okuma-only tool'ları çağırır.
        // Sipariş oluşturma / şikayet kaydı gibi yan-etkili işlemler bu kanalda YOKTUR.
        // ws://host/chat/realtime-native/{sessionId?}
        app.Map("/chat/realtime-native/{sessionId?}", HandleRealtimeNativeAsync);

        return app;
    }

    private static async Task HandleRealtimeAsync(
        HttpContext httpContext,
        string? sessionId,
        IRealtimeBridge bridge,
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
            await CloseGracefullyAsync(ws);
        }
    }

    private static async Task HandleRealtimeNativeAsync(
        HttpContext httpContext,
        string? sessionId,
        IRealtimeNativeBridge bridge,
        ILogger<RealtimeNativeBridge> logger)
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "RealtimeNative endpoint hata session={Sid}", sid);
        }
        finally
        {
            await CloseGracefullyAsync(ws);
        }
    }

    private static async Task CloseGracefullyAsync(System.Net.WebSockets.WebSocket ws)
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
