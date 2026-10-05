// Eskalasyon ve temsilci devralma olayları görüşmeyi "insan dahil" olarak işaretler (containment ölçümü).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.IntegrationTests;

public class HumanInvolvementTrackingServiceTests
{
    [Fact]
    public async Task EscalationAndTakeover_MarkTheConversation()
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var escalated = (await sessions.GetOrCreateAsync(null)).SessionId;
        var takenOver = (await sessions.GetOrCreateAsync(null)).SessionId;
        var botOnly = (await sessions.GetOrCreateAsync(null)).SessionId;
        var escalations = Substitute.For<IEscalationSink>();
        var modes = Substitute.For<IChatModeRegistry>();
        var service = new HumanInvolvementTrackingService(escalations, modes, new HumanInvolvementTracker(sessions),
            NullLogger<HumanInvolvementTrackingService>.Instance);

        await service.StartAsync(CancellationToken.None);
        escalations.RequestCreated += Raise.Event<EventHandler<EscalationRequest>>(escalations, new EscalationRequest { SessionId = escalated });
        modes.ModeChanged += Raise.Event<EventHandler<ChatSessionState>>(modes, new ChatSessionState { SessionId = takenOver, Mode = ChatMode.Human });
        modes.ModeChanged += Raise.Event<EventHandler<ChatSessionState>>(modes, new ChatSessionState { SessionId = botOnly, Mode = ChatMode.Bot });

        async Task<bool> Involved(string id) => (await sessions.GetAsync(id))!.State.HumanInvolved;
        for (var i = 0; i < 50 && !(await Involved(escalated) && await Involved(takenOver)); i++) await Task.Delay(20);

        (await Involved(escalated)).Should().BeTrue();
        (await Involved(takenOver)).Should().BeTrue();
        (await Involved(botOnly)).Should().BeFalse("bota geri dönüş insan dahil olma değildir");
        await service.StopAsync(CancellationToken.None);
    }
}
