// E-postaya eklenen fotoğraf, sohbetteki fotoğraf akışıyla AYNI yoldan geçer (doğrulama, metadata temizleme, görsel analiz, sahiplik).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class InboundEmailAttachmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Rig(InboundEmailService Svc, IChatPort Chat, IChatAttachmentPort Attachments);

    private static Rig Build(Func<byte[], AttachmentUploadResult>? upload = null, Action<InboundEmailOptions>? configure = null)
    {
        var customers = Substitute.For<ICustomerRepository>();
        customers.FindIdByEmailAsync("ayse@example.com", Arg.Any<CancellationToken>()).Returns(1001L);
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("ayse@example.com");
        var chat = Substitute.For<IChatPort>();
        chat.HandleAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>()).Returns(new ChatResponse("tamam", "sess-1"));
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetAllSessionsAsync("1001", Arg.Any<CancellationToken>()).Returns([]);
        var sender = Substitute.For<IEmailSender>();
        sender.IsEnabled.Returns(true);
        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string?>()).Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string?>() ?? "", [], null));

        var n = 0;
        var attachments = Substitute.For<IChatAttachmentPort>();
        attachments.UploadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ci => (upload ?? (_ => new AttachmentUploadResult(AttachmentUploadStatus.Ok, $"att-{++n}", "sess-1", "kupa")))(ci.Arg<byte[]>()));

        var o = new InboundEmailOptions { Enabled = true };
        configure?.Invoke(o);
        var svc = new InboundEmailService(customers, sessions, chat, guard, new InMemoryNotificationLedger(), sender, Options.Create(o),
            Options.Create(new EmailOptions { Enabled = true, FromAddress = "destek@example.com" }), NullLogger<InboundEmailService>.Instance,
            attachments: attachments);
        return new Rig(svc, chat, attachments);
    }

    private static InboundEmail Mail(params InboundAttachment[] files) =>
        new("ayse@example.com", "Kupa", "Ürün kırık geldi", $"m-{Guid.NewGuid():N}", true, null, files);

    private static InboundAttachment Img(string name = "a.jpg", string type = "image/jpeg") => new(name, type, [1, 2, 3]);

    [Fact]
    public async Task Photos_AreUploadedForTheCustomer_AndAttachedToTheTurn()
    {
        var r = Build();

        await r.Svc.HandleAsync(Mail(Img("1.jpg"), Img("2.png", "image/png")), Ct);

        await r.Attachments.Received(2).UploadAsync(Arg.Any<string?>(), "1001", Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        await r.Chat.Received(1).HandleAsync(
            Arg.Is<ChatRequest>(q => q.AttachmentIds != null && q.AttachmentIds.SequenceEqual(new[] { "att-1", "att-2" }) && q.SessionId == "sess-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonImageFiles_AreNotUploaded()
    {
        var r = Build();

        await r.Svc.HandleAsync(Mail(new InboundAttachment("virus.exe", "application/octet-stream", [1]), Img()), Ct);

        await r.Attachments.Received(1).UploadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheFirstFewPhotos_AreTaken()
    {
        var r = Build(configure: o => o.MaxAttachments = 2);

        await r.Svc.HandleAsync(Mail(Img("1.jpg"), Img("2.jpg"), Img("3.jpg")), Ct);

        await r.Attachments.Received(2).UploadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARejectedPhoto_DoesNotBlockTheTextTurn()
    {
        var r = Build(upload: _ => new AttachmentUploadResult(AttachmentUploadStatus.UnsupportedType, Error: "yalnızca JPEG/PNG"));

        var outcome = await r.Svc.HandleAsync(Mail(Img()), Ct);

        outcome.Should().Be(InboundEmailOutcome.Replied);
        await r.Chat.Received(1).HandleAsync(Arg.Is<ChatRequest>(q => q.AttachmentIds == null || q.AttachmentIds.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUploadException_DoesNotBlockTheTextTurn()
    {
        var r = Build(upload: _ => throw new InvalidOperationException("vision down"));

        (await r.Svc.HandleAsync(Mail(Img()), Ct)).Should().Be(InboundEmailOutcome.Replied);
    }

    [Fact]
    public async Task APhotoOnlyMail_WithNoText_IsStillProcessed()
    {
        var r = Build();
        var mail = Mail(Img()) with { Text = "" };

        (await r.Svc.HandleAsync(mail, Ct)).Should().Be(InboundEmailOutcome.Replied);
        await r.Chat.Received(1).HandleAsync(Arg.Is<ChatRequest>(q => q.Query.Length > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAnyPhotosOrText_TheMailIsEmpty()
    {
        var r = Build();

        (await r.Svc.HandleAsync(Mail() with { Text = "" }, Ct)).Should().Be(InboundEmailOutcome.Empty);
    }
}
