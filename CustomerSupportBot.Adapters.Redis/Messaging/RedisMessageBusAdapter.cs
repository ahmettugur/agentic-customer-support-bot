// Messaging/RedisMessageBusAdapter.cs
// IMessageBusPort Redis implementasyonu.
// StackExchange.Redis pub/sub üzerinden pod'lar arası mesajlaşma sağlar.

using CustomerSupportBot.Application.Ports.Driven.Messaging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.Messaging;

public sealed class RedisMessageBusAdapter : IMessageBusPort
{
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisMessageBusAdapter> _logger;

    public string NodeId { get; } = Guid.NewGuid().ToString("N");

    public RedisMessageBusAdapter(
        IConnectionMultiplexer redis,
        ILogger<RedisMessageBusAdapter> logger)
    {
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public void Publish(string channel, string jsonPayload)
    {
        try
        {
            _subscriber.Publish(RedisChannel.Literal(channel), jsonPayload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MessageBus] Redis publish başarısız: {Channel}", channel);
        }
    }

    public void Subscribe(string channel, Action<string> handler)
    {
        _subscriber.Subscribe(RedisChannel.Literal(channel), (_, value) =>
        {
            try
            {
                handler(value.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MessageBus] Handler hatası: {Channel}", channel);
            }
        });
    }
}
