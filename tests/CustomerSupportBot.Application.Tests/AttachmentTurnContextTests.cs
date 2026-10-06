// Mesaja eklenen fotoğrafların sohbet turuna (ajanların gördüğü metne) eklenmesi.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Attachments;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class AttachmentTurnContextTests
{
    private static async Task<(InMemoryAttachmentStore Store, ChatAttachment Mine, ChatAttachment Foreign, ChatAttachment OtherSession)> SeedAsync()
    {
        var store = new InMemoryAttachmentStore();
        var mine = new ChatAttachment { SessionId = "s1", CustomerId = "1001", ContentType = "image/jpeg", Description = "Kulpu kırık kupa." };
        var foreign = new ChatAttachment { SessionId = "s1", CustomerId = "2002", ContentType = "image/jpeg", Description = "başkası" };
        var otherSession = new ChatAttachment { SessionId = "s2", CustomerId = "1001", ContentType = "image/png", Description = "diğer oturum" };
        foreach (var a in new[] { mine, foreign, otherSession }) await store.SaveAsync(a);
        return (store, mine, foreign, otherSession);
    }

    [Fact]
    public async Task OnlyThisSessionsAndCustomersPhotos_AreAccepted()
    {
        var (store, mine, foreign, other) = await SeedAsync();

        var resolved = await AttachmentTurnContext.ResolveAsync(
            store, [mine.Id, foreign.Id, other.Id, "bilinmeyen"], "s1", "1001", maxPerMessage: 3, CancellationToken.None);

        resolved.Select(a => a.Id).Should().Equal(mine.Id);
    }

    [Fact]
    public async Task PerMessageLimit_IsApplied_AndDuplicatesIgnored()
    {
        var store = new InMemoryAttachmentStore();
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var a = new ChatAttachment { SessionId = "s1", CustomerId = "1001", ContentType = "image/jpeg" };
            await store.SaveAsync(a, TestContext.Current.CancellationToken);
            ids.Add(a.Id);
        }

        var resolved = await AttachmentTurnContext.ResolveAsync(
            store, [ids[0], ids[0], .. ids], "s1", "1001", maxPerMessage: 3, CancellationToken.None);

        resolved.Select(a => a.Id).Should().Equal(ids[0], ids[1], ids[2]);
    }

    [Fact]
    public void Compose_AppendsTheAnalysis_ToTheMessage()
    {
        var text = AttachmentTurnContext.Compose("Ürün kırık geldi",
        [
            new ChatAttachment { Description = "Kulpu kırık kupa." },
            new ChatAttachment { Description = null }
        ]);

        text.Should().StartWith("Ürün kırık geldi");
        text.Should().Contain("[Müşterinin eklediği fotoğraf 1 — otomatik analiz]: Kulpu kırık kupa.");
        text.Should().Contain("[Müşterinin eklediği fotoğraf 2 — otomatik analiz yapılamadı; temsilci fotoğrafı görebilir]");
    }

    [Fact]
    public void Compose_WithoutPhotos_LeavesTheMessageUnchanged()
    {
        AttachmentTurnContext.Compose("merhaba", []).Should().Be("merhaba");
    }

    [Fact]
    public void Compose_SinglePhoto_IsNotNumbered()
    {
        AttachmentTurnContext.Compose("bak", [new ChatAttachment { Description = "Kupa." }])
            .Should().EndWith("[Müşterinin eklediği fotoğraf — otomatik analiz]: Kupa.");
    }
}
