// Sohbet içi bildirim dağıtıcısı: müşteri bazlı abonelik + pod'lar arası yayın.

using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Application.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Application.Tests;

public class CustomerNotificationHubTests
{
    /// <summary>Redis gibi davranan paylaşılan yol: yayın, kendi düğümü dahil tüm abonelere gider.</summary>
    private sealed class SharedBus
    {
        private readonly List<(string Channel, Action<string> Handler)> _subs = [];
        public IMessageBusPort CreateNode() => new Node(this);

        private sealed class Node(SharedBus bus) : IMessageBusPort
        {
            public string NodeId { get; } = Guid.NewGuid().ToString("N");
            public void Publish(string channel, string json)
            {
                foreach (var (c, h) in bus._subs.ToArray()) if (c == channel) h(json);
            }
            public void Subscribe(string channel, Action<string> handler) => bus._subs.Add((channel, handler));
        }
    }

    private static CustomerNotification Sample(string text = "Siparişiniz kargoda") =>
        new(CustomerNotificationKinds.OrderShipped, "Kargoya verildi", text, "1044", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));

    private static CustomerNotificationHub Hub(IMessageBusPort? bus) =>
        new(bus, NullLogger<CustomerNotificationHub>.Instance);

    [Fact]
    public async Task Publish_ReachesOnlyTheSubscribersOfThatCustomer()
    {
        var hub = Hub(new SharedBus().CreateNode());
        var mine = new List<CustomerNotification>();
        var others = new List<CustomerNotification>();
        using var a = hub.Subscribe("1001", n => { mine.Add(n); return Task.CompletedTask; });
        using var b = hub.Subscribe("2002", n => { others.Add(n); return Task.CompletedTask; });

        hub.Publish("1001", Sample());
        await Task.Yield();

        mine.Should().ContainSingle().Which.ReferenceId.Should().Be("1044");
        others.Should().BeEmpty("başka müşterinin bildirimi bu aboneye sızmamalı");
    }

    [Fact]
    public void Publish_FromAnotherPod_IsDeliveredToTheLocalSubscriber()
    {
        var shared = new SharedBus();
        var podA = Hub(shared.CreateNode());
        var podB = Hub(shared.CreateNode());
        var received = new List<CustomerNotification>();
        using var sub = podB.Subscribe("1001", n => { received.Add(n); return Task.CompletedTask; });

        podA.Publish("1001", Sample("pod A yayınladı"));

        received.Should().ContainSingle().Which.Text.Should().Be("pod A yayınladı");
    }

    [Fact]
    public void Dispose_StopsDelivery()
    {
        var hub = Hub(new SharedBus().CreateNode());
        var received = new List<CustomerNotification>();
        var sub = hub.Subscribe("1001", n => { received.Add(n); return Task.CompletedTask; });
        sub.Dispose();

        hub.Publish("1001", Sample());

        received.Should().BeEmpty();
    }

    [Fact]
    public void WithoutABus_DeliversLocally()
    {
        var hub = Hub(bus: null);
        var received = new List<CustomerNotification>();
        using var sub = hub.Subscribe("1001", n => { received.Add(n); return Task.CompletedTask; });

        hub.Publish("1001", Sample());

        received.Should().ContainSingle();
    }

    [Fact]
    public void AThrowingSubscriber_DoesNotStarveTheOthers()
    {
        var hub = Hub(bus: null);
        var received = new List<CustomerNotification>();
        using var bad = hub.Subscribe("1001", _ => throw new InvalidOperationException("boom"));
        using var good = hub.Subscribe("1001", n => { received.Add(n); return Task.CompletedTask; });

        var act = () => hub.Publish("1001", Sample());

        act.Should().NotThrow();
        received.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Publish_WithoutACustomer_IsIgnored(string customerId)
    {
        var hub = Hub(bus: null);
        var received = new List<CustomerNotification>();
        using var sub = hub.Subscribe("1001", n => { received.Add(n); return Task.CompletedTask; });

        hub.Publish(customerId, Sample());

        received.Should().BeEmpty();
    }
}
