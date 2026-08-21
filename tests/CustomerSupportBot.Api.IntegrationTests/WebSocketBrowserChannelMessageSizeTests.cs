// WebSocket kanalının mesaj boyutu SINIRI.
//
// Alım döngüsü, EndOfMessage gelene kadar parçaları bir MemoryStream'de biriktirir. Bir sınır
// yoksa parçaları hiç bitirmeyen (kasıtlı ya da bozuk) bir istemci sunucu belleğini sınırsız
// büyütebilirdi — tek bir bağlantı, tamamlanmayan tek bir "mesaj" ile process'i tüketebilirdi.
//
// Gerçek bir soket açmadan ölçmek için WebSocket'in soyut sınıfını taklit ediyoruz: sahte,
// önceden hazırlanmış parçaları ReceiveAsync üzerinden sırayla döndürür.

using System.Net.WebSockets;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Api.IntegrationTests;

public class WebSocketBrowserChannelMessageSizeTests
{
    /// <summary>Sabit boyutlu parçaları, her biri ayrı bir ReceiveAsync çağrısında döndüren sahte soket.</summary>
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Chunk, bool EndOfMessage)> _chunks;
        public bool Closed { get; private set; }
        public WebSocketCloseStatus? CloseStatusSeen { get; private set; }

        public ScriptedWebSocket(IEnumerable<(byte[] Chunk, bool EndOfMessage)> chunks)
            => _chunks = new Queue<(byte[], bool)>(chunks);

        public override WebSocketCloseStatus? CloseStatus => CloseStatusSeen;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => Closed ? WebSocketState.Closed : WebSocketState.Open;
        public override string? SubProtocol => null;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken ct)
        {
            if (_chunks.Count == 0)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

            var (chunk, eom) = _chunks.Dequeue();
            chunk.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(chunk.Length, WebSocketMessageType.Binary, eom));
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            Closed = true;
            CloseStatusSeen = closeStatus;
            return Task.CompletedTask;
        }

        public override void Abort() => Closed = true;
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType t, bool e, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>
    /// ASIL BULGU. EndOfMessage'a hiç ulaşmayan bir istemci — her parça küçük ama sayısı
    /// sınırı aşıyor. Sınır olmadan bu döngü sonsuza kadar bellekte birikirdi; test bir
    /// noktada durmalı ve bağlantıyı kapatmalı.
    /// </summary>
    [Fact]
    public async Task MessageExceedingTheLimit_ClosesTheConnection_InsteadOfBufferingForever()
    {
        // 5 milyon adet 1 baytlık parça, hiçbiri EndOfMessage=true değil — 4 MB sınırını
        // rahatça aşar. Sınır çalışıyorsa döngü çok daha erken durur.
        const int chunkSize = 8 * 1024;  // alım tamponu 16KB, uyumlu kalmalı
        var chunks = Enumerable.Range(0, 600)  // 600 * 8KB = ~4.7 MB > 4 MB sınır
            .Select(_ => (new byte[chunkSize], false))
            .ToList();

        var socket = new ScriptedWebSocket(chunks);
        var channel = new WebSocketBrowserChannel(socket);

        var received = new List<BrowserMessage>();
        await foreach (var msg in channel.ReceiveMessagesAsync(TestContext.Current.CancellationToken))
            received.Add(msg);

        socket.Closed.Should().BeTrue("sınırı aşan bir mesaj bağlantıyı kapatmalı — sonsuza kadar biriktirmemeli");
        socket.CloseStatusSeen.Should().Be(WebSocketCloseStatus.MessageTooBig);
        received.Should().ContainSingle(m => m.Kind == BrowserMessageKind.Closed);
    }

    /// <summary>Karşı yön: sınır altındaki normal bir mesaj olağan şekilde teslim edilmeli.</summary>
    [Fact]
    public async Task MessageWithinTheLimit_IsDeliveredNormally()
    {
        var payload = new byte[1024];
        var chunks = new List<(byte[], bool)> { (payload, true) };
        var socket = new ScriptedWebSocket(chunks);
        var channel = new WebSocketBrowserChannel(socket);

        var received = new List<BrowserMessage>();
        await foreach (var msg in channel.ReceiveMessagesAsync(TestContext.Current.CancellationToken))
            received.Add(msg);

        received.Should().ContainSingle(m => m.Kind == BrowserMessageKind.Binary && m.Data!.Length == 1024);
        socket.Closed.Should().BeFalse();
    }
}
