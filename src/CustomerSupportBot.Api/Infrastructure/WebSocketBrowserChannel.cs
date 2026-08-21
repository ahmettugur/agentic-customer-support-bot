// Api/Infrastructure/WebSocketBrowserChannel.cs
// IBrowserChannel driven port'unun WebSocket adaptörü.
// RealtimeEndpoints bu sınıfı oluşturur; Application katmanı IBrowserChannel port'una bağımlıdır.

using System.Net.WebSockets;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Api.Infrastructure;

internal sealed class WebSocketBrowserChannel : IBrowserChannel
{
    private readonly WebSocket _ws;

    /// <summary>
    /// Tek bir mantıksal WebSocket mesajının en fazla taşıyabileceği bayt sayısı.
    ///
    /// <para>
    /// Alım döngüsü <c>EndOfMessage</c> gelene kadar parçaları bir <see cref="MemoryStream"/>'de
    /// biriktirir. Bu sınır yoksa parçaları hiç bitirmeyen (kasıtlı ya da bozuk) bir istemci
    /// sunucu belleğini sınırsız büyütebilirdi — tek bir bağlantı, tamamlanmayan tek bir
    /// "mesaj" ile process'i tüketebilirdi. Sesli akıştaki gerçek parçalar (mikrofon
    /// chunk'ları) birkaç KB'lik ayrık mesajlardır; 4 MB bu akışı asla sınırlamaz.
    /// </para>
    /// </summary>
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public WebSocketBrowserChannel(WebSocket ws) => _ws = ws;

    public bool IsOpen => _ws.State == WebSocketState.Open;

    public async IAsyncEnumerable<BrowserMessage> ReceiveMessagesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            var tooLarge = false;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield return new BrowserMessage(BrowserMessageKind.Closed, null);
                    yield break;
                }

                if (ms.Length + result.Count > MaxMessageBytes)
                {
                    // Sınır aşılır aşılmaz döngüden HEMEN çıkıyoruz — parçaların bitmesini
                    // (EndOfMessage) beklemek kötü niyetli bir istemcinin bağlantıyı süresiz
                    // açık tutmasına izin verirdi; bellek büyümesi dursa bile bu başlı başına
                    // bir kaynak tüketimi olurdu.
                    tooLarge = true;
                    break;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (tooLarge)
            {
                await _ws.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    $"Mesaj {MaxMessageBytes} bayt sınırını aştı.",
                    ct).ConfigureAwait(false);
                yield return new BrowserMessage(BrowserMessageKind.Closed, null);
                yield break;
            }

            var payload = ms.ToArray();
            if (payload.Length == 0) continue;

            var kind = result.MessageType == WebSocketMessageType.Binary
                ? BrowserMessageKind.Binary
                : BrowserMessageKind.Text;

            yield return new BrowserMessage(kind, payload);
        }
    }

    public async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task SendBinaryAsync(byte[] data, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        await _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct);
    }

    public async Task CloseAsync(string reason, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct); }
        catch { /* best effort */ }
    }
}
