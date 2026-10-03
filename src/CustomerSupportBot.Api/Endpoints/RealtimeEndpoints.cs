using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Chat;

namespace CustomerSupportBot.Api.Endpoints;

public static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        // Sesli kanal — gpt-realtime modeli kendisi konuşur ve tool'ları çağırır. Sipariş oluşturma,
        // iptal, iade ve şikayet kaydı yazılı sohbetle aynı insan onayına gönderilir.
        // ws://host/chat/realtime-native/{sessionId?}
        //
        // (Eskiden ayrıca /chat/realtime "köprü modu" vardı — model yalnızca STT/TTS yapıyor,
        // yanıtı agent pipeline üretiyordu. Kaldırıldı; tek sesli mod budur.)
        //
        // "chat" rate-limit'i BİLEREK uygulanıyor: her WS bağlantısı gerçek bir OpenAI
        // Realtime API oturumu açar (yazılı chat'ten daha maliyetli). Limitsiz bırakılırsa
        // geçerli/sızmış bir müşteri JWT'siyle saniyede çok sayıda bağlantı açılıp doğrudan
        // maliyet-bombası DoS'una yol açar.
        app.Map("/chat/realtime-native/{sessionId?}", HandleRealtimeNativeAsync)
            .RequireAuthorization("Customer")
            .RequireRateLimiting("chat");

        return app;
    }

    /// <summary>
    /// Login'li müşterinin doğrulanmış kimliği — yazılı chat ile AYNI claim
    /// (bkz. <c>ChatEndpoints</c>). Oturuma bağlanır ve sipariş tool'ları bunu kullanır.
    /// </summary>
    private static string? AuthenticatedCustomerId(HttpContext httpContext) =>
        httpContext.User.FindFirst("linked_customer_id")?.Value;

    private static async Task HandleRealtimeNativeAsync(
        HttpContext httpContext,
        string? sessionId,
        IRealtimeNativeBridge bridge,
        ILoggerFactory loggerFactory)
    {
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket bağlantısı bekleniyor.");
            return;
        }

        // Soket AÇILMADAN önce: kabul edildikten sonra durum kodu yoktur ve her bağlantı
        // gerçek bir OpenAI Realtime oturumu açar.
        if (sessionId is not null && !SessionIdPolicy.IsValid(sessionId))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync(SessionIdPolicy.ErrorMessage);
            return;
        }

        var logger = loggerFactory.CreateLogger("RealtimeEndpoints");
        var ws = await httpContext.WebSockets.AcceptWebSocketAsync();
        var sid = sessionId ?? Guid.NewGuid().ToString();
        var channel = new WebSocketBrowserChannel(ws);

        try
        {
            await bridge.RunAsync(channel, sid, AuthenticatedCustomerId(httpContext), httpContext.RequestAborted);
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
