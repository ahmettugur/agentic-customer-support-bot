// Services/Persistence/PostgresChatBridge.cs
// HITL Live Takeover — Hibrit Channel<> + PostgreSQL history + Redis pub/sub.
//
// Davranış:
//   - Channel<ChatBridgeMessage> per abone — süreç-içi pub/sub.
//   - History kalıcılığı: BotTyping hariç tüm mesajlar DB'ye INSERT edilir.
//   - In-memory history buffer (ring 200) hâlâ var; restart'ta DB'den yüklenir.
//   - Reset: DB'deki kayıtları silmiyoruz (audit). Sadece in-memory state'i temizler.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Her Broadcast çağrısı Redis'e yayın yapar.
//   - Uzak pod Redis mesajını alır ve kendi lokal Channel'larına iletir.
//   - DB yazımı yalnızca orijinal pod tarafından yapılır; uzak işleyici sadece broadcast eder.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresChatBridge : IChatBridge
{
    private const int HistoryCapacity = 200;

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresChatBridge> _logger;
    private readonly IMessageBusPort _messageBus;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> _toAdmin = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> _toUser = new();
    private readonly ConcurrentDictionary<string, List<ChatBridgeMessage>> _history = new();
    private readonly ConcurrentDictionary<string, Lazy<Task>> _hydratedSessions = new();

    public PostgresChatBridge(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresChatBridge> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _messageBus = messageBus;
        _messageBus.Subscribe("csbot:bridge:touser", val => OnRemoteBridge(_toUser, val));
        _messageBus.Subscribe("csbot:bridge:toadmin", val => OnRemoteBridge(_toAdmin, val));
    }

    // ─── Publish ───

    public async Task PublishUserMessageAsync(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.User, Text = text };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
    }

    public async Task PublishAdminMessageAsync(string sessionId, string humanAgent, string text)
{
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.Admin, HumanAgent = humanAgent, Text = text };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toUser, sessionId, msg);
        PublishRedis("csbot:bridge:touser", msg);
    }

    public async Task PublishSystemMessageAsync(string sessionId, string text)
{
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toAdmin, sessionId, msg);
        Broadcast(_toUser, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
        PublishRedis("csbot:bridge:touser", msg);
    }

    public async Task PublishAdminOnlyMessageAsync(string sessionId, string text)
{
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
        // _toUser'a gönderilmez — müşteri görmez
    }

    public async Task PublishBotMessageAsync(string sessionId, string text)
{
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.Bot, Text = text };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toUser, sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:touser", msg);
        PublishRedis("csbot:bridge:toadmin", msg);
    }

    public void PublishBotTyping(string sessionId, bool on)
    {
        // Transient — DB'ye yazılmaz.
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.BotTyping, Text = on ? "on" : "off" };
        Broadcast(_toUser, sessionId, msg);
        PublishRedis("csbot:bridge:touser", msg);
    }

    public async Task RecordBotExchangeAsync(string sessionId, string userQuery, string botResponse)
    {
        if (!string.IsNullOrWhiteSpace(userQuery))
        {
            await AppendAsync(sessionId, new ChatBridgeMessage
            {
                SessionId = sessionId,
                Sender = ChatBridgeSender.User,
                Text = userQuery
            }).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(botResponse))
        {
            await AppendAsync(sessionId, new ChatBridgeMessage
            {
                SessionId = sessionId,
                Sender = ChatBridgeSender.Bot,
                Text = botResponse
            }).ConfigureAwait(false);
        }
    }

    // ─── Subscribe ───

    public async IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureSessionHydratedAsync(sessionId).ConfigureAwait(false);
        var channel = CreateAndRegister(_toAdmin, sessionId);
        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(ct))
            {
                yield return msg;
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            Unregister(_toAdmin, sessionId, channel);
        }
    }

    public async IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureSessionHydratedAsync(sessionId).ConfigureAwait(false);
        var channel = CreateAndRegister(_toUser, sessionId);
        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(ct))
            {
                yield return msg;
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            Unregister(_toUser, sessionId, channel);
        }
    }

    // ─── History ───

    public async Task<IReadOnlyList<ChatBridgeMessage>> GetHistoryAsync(string sessionId, int take = 50)
    {
        await EnsureSessionHydratedAsync(sessionId).ConfigureAwait(false);
        if (!_history.TryGetValue(sessionId, out var list)) return Array.Empty<ChatBridgeMessage>();

        // Kilit altında kopyala: Append aynı listeye kilit altında ekleyip kapasite taşınca
        // RemoveRange yapar; kilitsiz okuma küçülen listeyi indeksle gezerken patlıyordu.
        lock (list) return list.TakeLast(take).ToList();
    }

    public void Reset(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
        _hydratedSessions.TryRemove(sessionId, out _);
        if (_toAdmin.TryRemove(sessionId, out var a))
            foreach (var ch in a.Keys) ch.Writer.TryComplete();
        if (_toUser.TryRemove(sessionId, out var u))
            foreach (var ch in u.Keys) ch.Writer.TryComplete();
        // NOT: DB kayıtları silinmiyor (audit trail).
    }

    // ─── Internals ───

    private Channel<ChatBridgeMessage> CreateAndRegister(
        ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> registry,
        string sessionId)
    {
        var channel = Channel.CreateUnbounded<ChatBridgeMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var set = registry.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>());
        set[channel] = 0;
        return channel;
    }

    /// <summary>
    /// Aboneliği (WebSocket/SSE kapandığında) kaydından çıkarır.
    ///
    /// <para>
    /// Eskiden bu adım hiç yoktu: kanal <c>ConcurrentBag</c>'e eklendikten sonra bir daha asla
    /// çıkarılmıyordu. <c>ConcurrentBag</c> zaten tekil eleman silmeyi desteklemez — bu yüzden
    /// kayıt yapısı silme destekleyen bir <c>ConcurrentDictionary</c>'ye (küme olarak
    /// kullanılıyor) çevrildi. Sonuç, sık bağlanıp kopan bir oturumda (sayfa yenileme, WebSocket
    /// yeniden bağlanma) tamamlanmış-ama-hâlâ-tutulan kanalların process ömrü boyunca birikmesiydi:
    /// bellek sınırsız büyür ve her <see cref="Broadcast"/> çağrısı, artık kimsenin okumadığı bu
    /// kanalları da tarayarak session'ın yaşı ilerledikçe yavaşlardı.
    /// </para>
    /// </summary>
    private static void Unregister(
        ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> registry,
        string sessionId,
        Channel<ChatBridgeMessage> channel)
    {
        if (registry.TryGetValue(sessionId, out var set))
            set.TryRemove(channel, out _);
    }

    private void Broadcast(
        ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> registry,
        string sessionId,
        ChatBridgeMessage msg)
    {
        if (!registry.TryGetValue(sessionId, out var bag)) return;
        foreach (var ch in bag.Keys)
        {
            ch.Writer.TryWrite(msg);
        }
    }

    private async Task AppendAsync(string sessionId, ChatBridgeMessage msg)
    {
        await EnsureSessionHydratedAsync(sessionId).ConfigureAwait(false);

        var list = _history.GetOrAdd(sessionId, _ => new List<ChatBridgeMessage>());
        lock (list)
        {
            list.Add(msg);
            if (list.Count > HistoryCapacity)
            {
                list.RemoveRange(0, list.Count - HistoryCapacity);
            }
        }

        // DB persist — INSERT (BotTyping zaten Append'e gönderilmiyor).
        try { await InsertAsync(msg).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Bridge] DB INSERT başarısız. Session={Session}, Sender={Sender}",
                sessionId, msg.Sender);
        }
    }

    private async Task InsertAsync(ChatBridgeMessage msg)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.ChatBridgeMessages.Add(new ChatBridgeMessageEntity
        {
            MessageId = msg.Id,
            SessionId = msg.SessionId,
            Sender = msg.Sender.ToString(),
            HumanAgent = msg.HumanAgent,
            Text = msg.Text,
            CreatedAt = msg.Timestamp
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Oturum geçmişinin DB'den yüklenmesini garanti eder — <b>tamamlanana kadar bekleyerek</b>.
    ///
    /// <para>
    /// Eskiden bir bayrak vardı ve DB okuması BAŞLAMADAN konuyordu: eşzamanlı gelen ikinci
    /// çağıran bayrağı görüp hemen devam ediyor, okuma sürerken boş geçmiş döndürüyor ya da
    /// mesajını cache'e ve DB'ye yazıyordu — ardından biten hydrate aynı mesajı DB'den bir kez
    /// daha ekliyor, admin paneli onu iki kez gösteriyordu. Değer artık işin kendisi
    /// (<c>Lazy&lt;Task&gt;</c>): herkes AYNI yüklemeyi bekler (bkz. PostgresSessionManager).
    /// </para>
    /// </summary>
    private async Task EnsureSessionHydratedAsync(string sessionId)
    {
        var work = _hydratedSessions.GetOrAdd(
            sessionId,
            id => new Lazy<Task>(() => HydrateSessionAsync(id), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await work.Value.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Girdiyi geri al — aksi halde geçici bir DB hatası bu session'ı process ömrü
            // boyunca "hydrate edildi ama boş" olarak kalıcı hale getirir (başarısız Task
            // cache'lenirse Lazy aynı hatayı sonsuza kadar yeniden fırlatırdı). Yalnızca bu
            // çağrının gördüğü girdi kaldırılır.
            _hydratedSessions.TryRemove(new KeyValuePair<string, Lazy<Task>>(sessionId, work));
            _logger.LogError(ex, "[Bridge] Session hydrate başarısız. Session={Session}", sessionId);
        }
    }

    private async Task HydrateSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.ChatBridgeMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Take(HistoryCapacity)
            .ToListAsync();

        if (rows.Count == 0) return;

        // Eskiden yeniye sırala
        rows.Reverse();
        var list = _history.GetOrAdd(sessionId, _ => new List<ChatBridgeMessage>());
        lock (list)
        {
            foreach (var e in rows)
            {
                var sender = Enum.TryParse<ChatBridgeSender>(e.Sender, ignoreCase: true, out var s)
                    ? s : ChatBridgeSender.System;

                list.Add(new ChatBridgeMessage
                {
                    Id = e.MessageId,
                    SessionId = e.SessionId,
                    Sender = sender,
                    HumanAgent = e.HumanAgent,
                    Text = e.Text,
                    Timestamp = e.CreatedAt
                });
            }
        }
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void OnRemoteBridge(
        ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> registry,
        string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;

            // nodeId field'ı varsa ve bu pod'dan geldiyse atla
            if (root.TryGetProperty("nodeId", out var nid) && nid.GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("SessionId").GetString()!;
            var senderStr = root.GetProperty("Sender").GetString() ?? "System";
            var sender = Enum.TryParse<ChatBridgeSender>(senderStr, ignoreCase: true, out var s)
                ? s : ChatBridgeSender.System;

            var msg = new ChatBridgeMessage
            {
                Id = root.TryGetProperty("Id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("N")[..12] : Guid.NewGuid().ToString("N")[..12],
                SessionId = sessionId,
                Sender = sender,
                Text = root.GetProperty("Text").GetString() ?? "",
                HumanAgent = root.TryGetProperty("HumanAgent", out var ha) && ha.ValueKind != JsonValueKind.Null ? ha.GetString() : null,
                Timestamp = root.TryGetProperty("Timestamp", out var ts) ? ts.GetDateTime() : DateTime.UtcNow
            };

            Broadcast(registry, sessionId, msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Bridge] Redis OnRemoteBridge parse hatası");
        }
    }

    private void PublishRedis(string channel, ChatBridgeMessage msg)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            msg.Id,
            msg.SessionId,
            Sender = msg.Sender.ToString(),
            msg.Text,
            msg.HumanAgent,
            msg.Timestamp
        };
        _messageBus.Publish(channel, JsonSerializer.Serialize(payload));
    }
}

