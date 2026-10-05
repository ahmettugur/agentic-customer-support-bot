// SMTP adaptörü — gerçek bir SMTP test sunucusuna (Mailpit) karşı. Sahte yok: gönderilen e-posta Mailpit'in
// API'sinden geri okunur ve alıcı/konu/gövde ölçülür.

using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Adapters.Email;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Email.Tests;

public sealed class MailpitFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.21")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
        .Build();

    public string Host => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(1025);
    public HttpClient Api { get; private set; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Api = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}") };
    }

    public async ValueTask DisposeAsync()
    {
        Api.Dispose();
        await _container.DisposeAsync();
    }
}

public class SmtpEmailSenderTests(MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SmtpEmailSender Sender() => new(Options.Create(new EmailOptions
    {
        Enabled = true,
        FromAddress = "destek@example.com",
        FromName = "Müşteri Destek",
        Smtp = new EmailOptions.SmtpOptions { Host = mailpit.Host, Port = mailpit.SmtpPort, Security = SmtpSecurity.None }
    }), NullLogger<SmtpEmailSender>.Instance);

    private async Task<JsonElement> FindAsync(string subject)
    {
        for (var i = 0; i < 20; i++)
        {
            var search = await mailpit.Api.GetFromJsonAsync<JsonElement>(
                $"/api/v1/search?query={Uri.EscapeDataString("subject:\"" + subject + "\"")}", Ct);
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
                return await mailpit.Api.GetFromJsonAsync<JsonElement>(
                    $"/api/v1/message/{messages[0].GetProperty("ID").GetString()}", Ct);
            await Task.Delay(100, Ct);
        }
        throw new Xunit.Sdk.XunitException($"'{subject}' konulu e-posta Mailpit'e ulaşmadı.");
    }

    [Fact]
    public async Task Send_DeliversTextAndHtml_ToTheRecipient()
    {
        var subject = $"Talebiniz onaylandı — İade {Guid.NewGuid():N}";

        await Sender().SendAsync(new EmailMessage("ayse@example.com", "Ayşe Yılmaz", subject,
            "Merhaba Ayşe,\n\nİade kaydınız oluşturuldu.", "<p>İade kaydınız <strong>oluşturuldu</strong>.</p>"), Ct);

        var mail = await FindAsync(subject);
        mail.GetProperty("To")[0].GetProperty("Address").GetString().Should().Be("ayse@example.com");
        mail.GetProperty("To")[0].GetProperty("Name").GetString().Should().Be("Ayşe Yılmaz");
        mail.GetProperty("From").GetProperty("Address").GetString().Should().Be("destek@example.com");
        mail.GetProperty("Text").GetString().Should().Contain("İade kaydınız oluşturuldu.");
        mail.GetProperty("HTML").GetString().Should().Contain("<strong>oluşturuldu</strong>");
    }

    [Fact]
    public async Task Send_WithUnreachableServer_Throws()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Enabled = true, FromAddress = "destek@example.com",
            Smtp = new EmailOptions.SmtpOptions { Host = "127.0.0.1", Port = 1, Security = SmtpSecurity.None, TimeoutSeconds = 2 }
        }), NullLogger<SmtpEmailSender>.Instance);

        var act = () => sender.SendAsync(new EmailMessage("a@example.com", null, "x", "y"), Ct);

        await act.Should().ThrowAsync<Exception>("çağıran (bildirim servisi) hatayı görüp talebi geri bırakmalı");
    }

    [Fact]
    public async Task NullSender_IsDisabled_AndSendsNothing()
    {
        var sender = new NullEmailSender(NullLogger<NullEmailSender>.Instance);

        sender.IsEnabled.Should().BeFalse();
        await sender.SendAsync(new EmailMessage("a@example.com", null, "x", "y"), Ct);
    }
}
