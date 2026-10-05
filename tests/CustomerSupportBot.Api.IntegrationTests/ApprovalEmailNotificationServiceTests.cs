// Onay kararı olayı → e-posta: barındırma servisi olaya abone olur, durunca bırakır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ApprovalEmailNotificationServiceTests
{
    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool IsEnabled => true;
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ApprovalRequest Decided(string id) => new()
    {
        Id = id, ToolName = WellKnown.ToolNames.OrderCancel, CustomerId = "1001",
        Status = ApprovalStatus.Rejected, DecisionReason = "Sipariş kargoya verilmiş."
    };

    [Fact]
    public async Task DecidedEvent_SendsTheEmail_UntilTheServiceStops()
    {
        var approvals = Substitute.For<IApprovalQueue>();
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("ayse@example.com");
        var sender = new FakeSender();
        var email = new ApprovalResultEmailService(sender, new InMemoryNotificationLedger(), customers,
            Options.Create(new EmailOptions { Enabled = true, FromAddress = "destek@example.com", Smtp = new() { Host = "h" } }),
            NullLogger<ApprovalResultEmailService>.Instance);
        var service = new ApprovalEmailNotificationService(approvals, email, NullLogger<ApprovalEmailNotificationService>.Instance);

        await service.StartAsync(CancellationToken.None);
        approvals.RequestDecided += Raise.Event<EventHandler<ApprovalRequest>>(approvals, Decided("a1"));
        for (var i = 0; i < 50 && sender.Sent.Count == 0; i++) await Task.Delay(20);

        sender.Sent.Should().ContainSingle().Which.Subject.Should().Contain("reddedildi");

        await service.StopAsync(CancellationToken.None);
        approvals.RequestDecided += Raise.Event<EventHandler<ApprovalRequest>>(approvals, Decided("a2"));
        await Task.Delay(200);
        sender.Sent.Should().ContainSingle("durduktan sonra olay işlenmez");
    }

    /// <summary>E-posta açık ama SMTP sunucusu yazılmamış: uygulama başlamamalı (sessizce gönderilmeyen e-posta yerine).</summary>
    [Fact]
    public void EnabledWithoutSmtpHost_FailsAtStartup()
    {
        using var root = new TestWebApplicationFactory();
        using var factory = root.WithWebHostBuilder(b => b
            .UseSetting("Email:Enabled", "true")
            .UseSetting("Email:FromAddress", "destek@example.com")
            .UseSetting("Email:Smtp:Host", ""));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Email:Smtp:Host*");
    }
}
