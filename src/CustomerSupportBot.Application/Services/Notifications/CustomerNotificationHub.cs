// Application/Services/Notifications/CustomerNotificationHub.cs
// Müşteri bazlı sohbet içi bildirim dağıtıcısı.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// Bildirimi yayınlayan pod ile müşterinin SSE akışını tutan pod farklı olabilir; bu yüzden yayın
/// <see cref="IMessageBusPort"/> üzerinden tek kanala gider ve her pod yalnızca kendi abonelerine dağıtır.
/// Bus'ın Unsubscribe'ı yok → kanala pod başına BİR KEZ abone olunur, müşteri aboneliği yerelde tutulur
/// (aksi halde her SSE bağlantısı kalıcı bir handler sızdırırdı). Bus yoksa yerel dağıtım yapılır.
/// </summary>
public sealed class CustomerNotificationHub
{
    public const string Channel = "csbot:customer-notify";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMessageBusPort? _bus;
    private readonly ILogger<CustomerNotificationHub> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<CustomerNotification, Task>>> _subscribers = new();

    private sealed record Envelope(string CustomerId, CustomerNotification Notification);

    public CustomerNotificationHub(IMessageBusPort? bus, ILogger<CustomerNotificationHub> logger)
    {
        _bus = bus;
        _logger = logger;
        _bus?.Subscribe(Channel, OnBusMessage);
    }

    /// <summary>Müşterinin açık akışına iletilmek üzere yayınlar. Hata yutulur: bildirim durum güncellemesini bozmamalı.</summary>
    public void Publish(string? customerId, CustomerNotification notification)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return;
        try
        {
            if (_bus is null) Dispatch(customerId, notification);
            else _bus.Publish(Channel, JsonSerializer.Serialize(new Envelope(customerId, notification), Json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] Sohbet içi bildirim yayınlanamadı | kind={Kind}", notification.Kind);
        }
    }

    public IDisposable Subscribe(string customerId, Func<CustomerNotification, Task> handler)
    {
        var id = Guid.NewGuid();
        _subscribers.GetOrAdd(customerId, _ => new()).TryAdd(id, handler);
        return new Subscription(this, customerId, id);
    }

    private void OnBusMessage(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
            if (envelope is { CustomerId.Length: > 0, Notification: not null })
                Dispatch(envelope.CustomerId, envelope.Notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] Bus mesajı çözümlenemedi");
        }
    }

    private void Dispatch(string customerId, CustomerNotification notification)
    {
        if (!_subscribers.TryGetValue(customerId, out var handlers)) return;
        foreach (var handler in handlers.Values)
        {
            try
            {
                var task = handler(notification);
                _ = task.ContinueWith(t => _logger.LogDebug(t.Exception?.GetBaseException(), "[Notify] Abone işleyicisi başarısız"),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Notify] Abone işleyicisi başarısız");
            }
        }
    }

    private sealed class Subscription(CustomerNotificationHub hub, string customerId, Guid id) : IDisposable
    {
        public void Dispose()
        {
            if (!hub._subscribers.TryGetValue(customerId, out var handlers)) return;
            handlers.TryRemove(id, out _);
            if (handlers.IsEmpty) hub._subscribers.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, Func<CustomerNotification, Task>>>(customerId, handlers));
        }
    }
}
