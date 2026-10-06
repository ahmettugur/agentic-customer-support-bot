// Yan etkili bir talep onaya gittiğinde oturumun fotoğrafları o onay kaydına bağlanıyor mu?

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ApprovalGateAttachmentTests
{
    private const string Tool = "complaint_registration_tool";

    private sealed record Fixture(SideEffectApprovalGate Gate, InMemoryApprovalQueue Queue, InMemoryAttachmentStore Store);

    private static Fixture Create()
    {
        var options = Options.Create(new ApprovalOptions { Enabled = true, ToolsRequiringApproval = [Tool, "return_request_tool"] });
        var queue = new InMemoryApprovalQueue(options, Substitute.For<IApprovalExecutionRouter>(), NullLogger<InMemoryApprovalQueue>.Instance);
        var store = new InMemoryAttachmentStore();
        return new Fixture(new SideEffectApprovalGate(queue, options, null, store), queue, store);
    }

    private static ApprovalContext Ctx => new("s1", null, "ürün kırık", CustomerId: "1001");

    private static async Task<ChatAttachment> AddPhotoAsync(
        InMemoryAttachmentStore store, string session = "s1", string customer = "1001", bool sent = true)
    {
        var a = new ChatAttachment
        {
            SessionId = session, CustomerId = customer, ContentType = "image/jpeg",
            SentAt = sent ? DateTime.UtcNow : null
        };
        await store.SaveAsync(a);
        return a;
    }

    private static Task<ToolResult> RequestAsync(Fixture f, string tool = Tool, string text = "kırık") =>
        f.Gate.ExecuteAsync(tool, new Dictionary<string, object?> { ["description"] = text },
            () => throw new InvalidOperationException("onaysız yürütülmemeli"), Ctx);

    private static async Task<ApprovalRequest> SinglePendingAsync(Fixture f, string tool = Tool) =>
        (await f.Queue.GetPendingAsync()).Single(r => r.ToolName == tool);

    [Fact]
    public async Task UnlinkedSessionPhotos_AreAddedToTheRequest_AndLinked()
    {
        var f = Create();
        var photo = await AddPhotoAsync(f.Store);
        var foreign = await AddPhotoAsync(f.Store, customer: "2002");
        var otherSession = await AddPhotoAsync(f.Store, session: "s2");

        await RequestAsync(f);

        var request = await SinglePendingAsync(f);
        request.Parameters[SideEffectApprovalGate.AttachmentIdsParameter].Should().BeEquivalentTo(new[] { photo.Id });
        (await f.Store.GetAsync(photo.Id, TestContext.Current.CancellationToken))!.ApprovalId.Should().Be(request.Id);
        (await f.Store.GetAsync(foreign.Id, TestContext.Current.CancellationToken))!.ApprovalId.Should().BeNull();
        (await f.Store.GetAsync(otherSession.Id, TestContext.Current.CancellationToken))!.ApprovalId.Should().BeNull();
    }

    [Fact]
    public async Task UploadedButNeverSentPhoto_IsNotAttached()
    {
        var f = Create();
        var unsent = await AddPhotoAsync(f.Store, sent: false);

        await RequestAsync(f);

        (await SinglePendingAsync(f)).Parameters.Should().NotContainKey(SideEffectApprovalGate.AttachmentIdsParameter);
        (await f.Store.GetAsync(unsent.Id, TestContext.Current.CancellationToken))!.ApprovalId.Should().BeNull();
    }

    [Fact]
    public async Task APhotoLinkedToOneRequest_DoesNotMoveToTheNextOne()
    {
        var f = Create();
        await AddPhotoAsync(f.Store);
        await RequestAsync(f);

        await RequestAsync(f, "return_request_tool", "iade");

        (await SinglePendingAsync(f, "return_request_tool")).Parameters
            .Should().NotContainKey(SideEffectApprovalGate.AttachmentIdsParameter);
    }

    [Fact]
    public async Task NewPhoto_DoesNotDefeatDuplicateDetection()
    {
        var f = Create();
        await RequestAsync(f);
        var late = await AddPhotoAsync(f.Store);

        await RequestAsync(f);

        (await f.Queue.GetPendingAsync(TestContext.Current.CancellationToken)).Should().ContainSingle("aynı talep yeni fotoğraf yüzünden mükerrer kayıt açmamalı");
        (await f.Store.GetAsync(late.Id, TestContext.Current.CancellationToken))!.ApprovalId.Should().BeNull("mevcut kaydın parametrelerinde olmayan fotoğraf ona bağlanmaz");
    }

    [Fact]
    public async Task WithoutPhotos_ParametersAreUntouched()
    {
        var f = Create();

        await RequestAsync(f);

        (await SinglePendingAsync(f)).Parameters.Keys.Should().Equal("description");
    }
}
