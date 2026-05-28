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

    private readonly ConcurrentDictionary<string, ConcurrentBag<Channel<ChatBridgeMessage>>> _toAdmin = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<Channel<ChatBridgeMessage>>> _toUser = new();
    private readonly ConcurrentDictionary<string, List<ChatBridgeMessage>> _history = new();
    private readonly ConcurrentDictionary<string, byte> _hydratedSessions = new();

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

    public void PublishUserMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.User, Text = text };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
    }

    public void PublishAdminMessage(string sessionId, string humanAgent, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.Admin, HumanAgent = humanAgent, Text = text };
        Append(sessionId, msg);
        Broadcast(_toUser, sessionId, msg);
        PublishRedis("csbot:bridge:touser", msg);
    }

    public void PublishSystemMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        Broadcast(_toUser, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
        PublishRedis("csbot:bridge:touser", msg);
    }

    public void PublishAdminOnlyMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
        // _toUser'a gönderilmez — müşteri görmez
    }

    public void PublishBotMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.Bot, Text = text };
        Append(sessionId, msg);
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

    public void RecordBotExchange(string sessionId, string userQuery, string botResponse)
    {
        if (!string.IsNullOrWhiteSpace(userQuery))
        {
            Append(sessionId, new ChatBridgeMessage
            {
                SessionId = sessionId,
                Sender = ChatBridgeSender.User,
                Text = userQuery
            });
        }
        if (!string.IsNullOrWhiteSpace(botResponse))
        {
            Append(sessionId, new ChatBridgeMessage
            {
                SessionId = sessionId,
                Sender = ChatBridgeSender.Bot,
                Text = botResponse
            });
        }
    }

    // ─── Subscribe ───

    public async IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureSessionHydrated(sessionId);
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
        }
    }

    public async IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureSessionHydrated(sessionId);
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
        }
    }

    // ─── History ───

    public IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50)
    {
        EnsureSessionHydrated(sessionId);
        return _history.TryGetValue(sessionId, out var list)
            ? list.TakeLast(take).ToList()
            : Array.Empty<ChatBridgeMessage>();
    }

    public void Reset(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
        _hydratedSessions.TryRemove(sessionId, out _);
        if (_toAdmin.TryRemove(sessionId, out var a))
            foreach (var ch in a) ch.Writer.TryComplete();
        if (_toUser.TryRemove(sessionId, out var u))
            foreach (var ch in u) ch.Writer.TryComplete();
        // NOT: DB kayıtları silinmiyor (audit trail).
    }

    // ─── Internals ───

    private Channel<ChatBridgeMessage> CreateAndRegister(
        ConcurrentDictionary<string, ConcurrentBag<Channel<ChatBridgeMessage>>> registry,
        string sessionId)
    {
        var channel = Channel.CreateUnbounded<ChatBridgeMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var bag = registry.GetOrAdd(sessionId, _ => new ConcurrentBag<Channel<ChatBridgeMessage>>());
        bag.Add(channel);
        return channel;
    }

    private void Broadcast(
        ConcurrentDictionary<string, ConcurrentBag<Channel<ChatBridgeMessage>>> registry,
        string sessionId,
        ChatBridgeMessage msg)
    {
        if (!registry.TryGetValue(sessionId, out var bag)) return;
        foreach (var ch in bag)
        {
            ch.Writer.TryWrite(msg);
        }
    }

    private void Append(string sessionId, ChatBridgeMessage msg)
    {
        EnsureSessionHydrated(sessionId);

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
        try { InsertAsync(msg).GetAwaiter().GetResult(); }
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

    private void EnsureSessionHydrated(string sessionId)
    {
        if (_hydratedSessions.ContainsKey(sessionId)) return;
        if (!_hydratedSessions.TryAdd(sessionId, 0)) return;

        try
        {
            HydrateSessionAsync(sessionId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
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
        ConcurrentDictionary<string, ConcurrentBag<Channel<ChatBridgeMessage>>> registry,
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

