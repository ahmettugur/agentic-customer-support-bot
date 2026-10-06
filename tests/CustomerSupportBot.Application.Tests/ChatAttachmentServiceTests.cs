// Sohbete fotoğraf yükleme — doğrulama, meta veri temizliği, görsel açıklama, sahiplik.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Attachments;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ChatAttachmentServiceTests
{
    /// <summary>En küçük geçerli JPEG yapısı: SOI + EXIF(Make) + DQT + SOS + veri + EOI.</summary>
    internal static byte[] JpegWithExif()
    {
        var exif = "Exif\0\0II*\0\x08\0\0\0\x01\0\x0F\x01\x02\0\x05\0\0\0\x1A\0\0\0\0\0\0\0Leak\0"u8.ToArray();
        var app1 = new byte[] { 0xFF, 0xE1, 0, (byte)(exif.Length + 2) }.Concat(exif);
        var dqt = new byte[] { 0xFF, 0xDB, 0, 4, 0, 1 };
        var sos = new byte[] { 0xFF, 0xDA, 0, 3, 1 };
        return [0xFF, 0xD8, .. app1, .. dqt, .. sos, 0x11, 0x22, 0xFF, 0xD9];
    }

    private sealed class Harness
    {
        public required ChatAttachmentService Service { get; init; }
        public required InMemoryAttachmentStore Store { get; init; }
        public required InMemorySessionManager Sessions { get; init; }
        public required IImageAnalysisPort Analyzer { get; init; }
        public required IInputGuard Guard { get; init; }
    }

    private static Harness Build(AttachmentOptions? options = null)
    {
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(locks);
        var store = new InMemoryAttachmentStore();
        var analyzer = Substitute.For<IImageAnalysisPort>();
        analyzer.DescribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Kutusu ezilmiş bir kupa, kulpu kırık.");
        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        return new Harness
        {
            Service = new ChatAttachmentService(store, sessions, locks, analyzer, guard,
                Options.Create(options ?? new AttachmentOptions()), NullLogger<ChatAttachmentService>.Instance),
            Store = store, Sessions = sessions, Analyzer = analyzer, Guard = guard
        };
    }

    [Fact]
    public async Task Upload_WithoutSession_CreatesOneBoundToTheCustomer()
    {
        var h = Build();

        var r = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        r.Status.Should().Be(AttachmentUploadStatus.Ok);
        r.SessionId.Should().NotBeNullOrWhiteSpace();
        r.Description.Should().Be("Kutusu ezilmiş bir kupa, kulpu kırık.");
        (await h.Sessions.GetAsync(r.SessionId!, TestContext.Current.CancellationToken))!.State.AuthenticatedCustomerId.Should().Be("1001");
    }

    [Fact]
    public async Task Upload_StoresTheImageWithoutMetadata()
    {
        var h = Build();

        var r = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);
        var stored = (await h.Store.GetAsync(r.AttachmentId!, TestContext.Current.CancellationToken))!;

        stored.ContentType.Should().Be("image/jpeg");
        stored.CustomerId.Should().Be("1001");
        stored.Data.AsSpan().IndexOf("Leak"u8).Should().Be(-1, "EXIF meta verisi saklanmamalı");
        var sentToModel = (byte[])h.Analyzer.ReceivedCalls().Single().GetArguments()[0]!;
        sentToModel.AsSpan().IndexOf("Leak"u8).Should().Be(-1, "görsel modele de meta verisiz görüntü gitmeli");
    }

    [Fact]
    public async Task Upload_ToAnotherCustomersSession_IsForbidden()
    {
        var h = Build();
        var owned = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        var r = await h.Service.UploadAsync(owned.SessionId, "2002", JpegWithExif(), TestContext.Current.CancellationToken);

        r.Status.Should().Be(AttachmentUploadStatus.Forbidden);
    }

    [Theory]
    [InlineData("GIF89a......")]
    [InlineData("<svg onload=alert(1)>")]
    public async Task NonImageContent_IsRejected_RegardlessOfName(string content)
    {
        var h = Build();

        var r = await h.Service.UploadAsync(null, "1001", System.Text.Encoding.ASCII.GetBytes(content), TestContext.Current.CancellationToken);

        r.Status.Should().Be(AttachmentUploadStatus.UnsupportedType);
        r.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EmptyAndOversizedFiles_AreRejected()
    {
        var h = Build(new AttachmentOptions { MaxBytes = 20 });

        (await h.Service.UploadAsync(null, "1001", [], TestContext.Current.CancellationToken)).Status.Should().Be(AttachmentUploadStatus.Empty);
        (await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken)).Status.Should().Be(AttachmentUploadStatus.TooLarge);
    }

    [Fact]
    public async Task SessionLimit_IsEnforced()
    {
        var h = Build(new AttachmentOptions { MaxPerSession = 2 });
        var first = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);
        await h.Service.UploadAsync(first.SessionId, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        (await h.Service.UploadAsync(first.SessionId, "1001", JpegWithExif(), TestContext.Current.CancellationToken)).Status
            .Should().Be(AttachmentUploadStatus.TooManyInSession);
    }

    [Fact]
    public async Task AnalysisFailure_StillStoresThePhoto()
    {
        var h = Build();
        h.Analyzer.DescribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("vision down"));

        var r = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        r.Status.Should().Be(AttachmentUploadStatus.Ok);
        r.Description.Should().BeNull();
        (await h.Store.GetAsync(r.AttachmentId!, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task Description_PassesThroughTheInputGuard()
    {
        var h = Build();
        h.Guard.Inspect(Arg.Any<string>())
            .Returns(new InputGuardResult(InputGuardVerdict.Allow, "Etikette telefon: ***-***-**12", ["pii_masked"], null));

        (await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken)).Description
            .Should().Be("Etikette telefon: ***-***-**12");
    }

    [Fact]
    public async Task InjectedDescription_IsDropped()
    {
        var h = Build();
        h.Guard.Inspect(Arg.Any<string>())
            .Returns(new InputGuardResult(InputGuardVerdict.Reject, "", ["injection"], "reddedildi"));

        var r = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        r.Status.Should().Be(AttachmentUploadStatus.Ok);
        r.Description.Should().BeNull();
    }

    [Fact]
    public async Task Get_ForCustomer_ReturnsOnlyTheirOwnPhoto()
    {
        var h = Build();
        var r = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);

        (await h.Service.GetAsync(r.AttachmentId!, "1001", TestContext.Current.CancellationToken)).Should().NotBeNull();
        (await h.Service.GetAsync(r.AttachmentId!, "2002", TestContext.Current.CancellationToken)).Should().BeNull();
        (await h.Service.GetAsync(r.AttachmentId!, null, TestContext.Current.CancellationToken)).Should().NotBeNull("personel uçları sahiplik kontrolü yapmaz");
    }

    [Fact]
    public async Task Disabled_RejectsUploads()
    {
        var h = Build(new AttachmentOptions { Enabled = false });

        (await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken)).Status.Should().Be(AttachmentUploadStatus.Disabled);
    }

    [Fact]
    public async Task DeleteUnsent_OnlyOwnAndOnlyBeforeSending()
    {
        var h = Build();
        var a = await h.Service.UploadAsync(null, "1001", JpegWithExif(), TestContext.Current.CancellationToken);
        var b = await h.Service.UploadAsync(a.SessionId, "1001", JpegWithExif(), TestContext.Current.CancellationToken);
        await h.Store.MarkSentAsync([b.AttachmentId!], DateTime.UtcNow, TestContext.Current.CancellationToken);

        (await h.Service.DeleteUnsentAsync(a.AttachmentId!, "2002", TestContext.Current.CancellationToken)).Should().BeFalse("başkasının fotoğrafı");
        (await h.Service.DeleteUnsentAsync(b.AttachmentId!, "1001", TestContext.Current.CancellationToken)).Should().BeFalse("gönderilmiş fotoğraf silinmez");
        (await h.Service.DeleteUnsentAsync(a.AttachmentId!, "1001", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await h.Store.GetAsync(a.AttachmentId!, TestContext.Current.CancellationToken)).Should().BeNull();
        (await h.Store.GetAsync(b.AttachmentId!, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }
}
