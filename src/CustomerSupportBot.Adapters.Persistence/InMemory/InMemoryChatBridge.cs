// Services/InMemoryChatBridge.cs
// HITL Live Takeover — System.Threading.Channels tabanlı pub/sub köprüsü.
//
// Yapısı (per session):
//   - _toAdmin[sid]: List<Channel> — her admin SSE abonesi için ayrı channel
//   - _toUser[sid]:  List<Channel> — her user SSE abonesi için ayrı channel
//   - _history[sid]: ring buffer (max 200) — admin "Üstlen" anında bağlam okur
//
// Production için: Redis Streams + consumer groups veya RabbitMQ.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public class InMemoryChatBridge : IChatBridge
{
    private const int HistoryCapacity = 200;

    // Her session için, her abone için ayrı bir channel tutuyoruz.
    // Aynı session'a birden fazla admin (veya user reload sonrası) bağlanabilsin.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> _toAdmin = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>> _toUser = new();
    private readonly ConcurrentDictionary<string, List<ChatBridgeMessage>> _history = new();
    private readonly ILogger<InMemoryChatBridge> _logger;

    public InMemoryChatBridge(ILogger<InMemoryChatBridge> logger)
    {
        _logger = logger;
    }

    // ─── Publish ───

    public void PublishUserMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.User,
            Text = text
        };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
    }

    public void PublishAdminMessage(string sessionId, string humanAgent, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.Admin,
            HumanAgent = humanAgent,
            Text = text
        };
        Append(sessionId, msg);
        Broadcast(_toUser, sessionId, msg);
    }

    public void PublishSystemMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.System,
            Text = text
        };
        Append(sessionId, msg);
        // System mesajları her iki tarafa da gider
        Broadcast(_toAdmin, sessionId, msg);
        Broadcast(_toUser, sessionId, msg);
    }

    public void PublishAdminOnlyMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.System,
            Text = text
        };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        // _toUser'a gönderilmez — müşteri görmez
    }

    public void PublishBotMessage(string sessionId, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.Bot,
            Text = text
        };
        Append(sessionId, msg);
        // Bot mesajları müşteriye push edilir; admin paneli history'den okur.
        Broadcast(_toUser, sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
    }

    public void PublishBotTyping(string sessionId, bool on)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId,
            Sender = ChatBridgeSender.BotTyping,
            Text = on ? "on" : "off"
        };
        // History'e yazma — transient kontrol sinyali. Sadece müşteri tarafına.
        Broadcast(_toUser, sessionId, msg);
    }

    public void RecordBotExchange(string sessionId, string userQuery, string botResponse)
    {
        // Sadece history'ye yaz — broadcast etmiyoruz çünkü zaten user kendi
        // Chat penceresinde görüyor. Admin bağlam için Get'leyecek.
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

    public IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50) =>
        _history.TryGetValue(sessionId, out var list)
            ? list.TakeLast(take).ToList()
            : Array.Empty<ChatBridgeMessage>();

    public void Reset(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
        // Channel'ları complete et (subscriber'lar yield bitirip çıkar)
        if (_toAdmin.TryRemove(sessionId, out var a))
            foreach (var ch in a.Keys) ch.Writer.TryComplete();
        if (_toUser.TryRemove(sessionId, out var u))
            foreach (var ch in u.Keys) ch.Writer.TryComplete();
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

    /// <summary>Aboneliği kaydından çıkarır — bkz. PostgresChatBridge.Unregister'daki gerekçe.</summary>
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
            // Channel kapalıysa (subscriber çıktı) sessizce skip
            ch.Writer.TryWrite(msg);
        }
    }

    private void Append(string sessionId, ChatBridgeMessage msg)
    {
        var list = _history.GetOrAdd(sessionId, _ => new List<ChatBridgeMessage>());
        lock (list)
        {
            list.Add(msg);
            if (list.Count > HistoryCapacity)
            {
                list.RemoveRange(0, list.Count - HistoryCapacity);
            }
        }
    }
}

