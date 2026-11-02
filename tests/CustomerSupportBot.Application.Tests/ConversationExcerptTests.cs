// Onay ve eskalasyon kartlarının konuşma özeti. Tur, onay kaydından SONRA yazılır (ölçüldü: 2-8 sn), yani tetikleyen cümle veritabanında
// onaydan önceki mesajlar arasında yoktur; kayıttaki metinden eklenir. Eskalasyon ise turun sonunda, mesajlarla neredeyse aynı anda oluşur.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Application.Tests;

public class ConversationExcerptTests
{
    private static ConversationMessage U(string t) => new(ConversationRoles.User, t);
    private static ConversationMessage A(string t) => new(ConversationRoles.Assistant, t);

    private static List<string> Lines(IReadOnlyList<ConversationExcerptLine> lines) =>
        lines.Select(l => $"{l.Speaker}: {l.Text}").ToList();

    [Fact]
    public void TheTriggeringSentence_ComesLast_EvenThoughItIsNotInTheDatabaseYet()
    {
        var lines = ConversationExcerpt.Build([U("3 adet"), A("Onaya göndermemi ister misiniz?")], "Evet, gönderebilirsiniz");

        Lines(lines).Should().Equal(
            "customer: 3 adet", "assistant: Onaya göndermemi ister misiniz?", "customer: Evet, gönderebilirsiniz");
    }

    [Fact]
    public void TheTriggeringSentence_IsNotRepeated_WhenItWasAlreadySaved()
    {
        Lines(ConversationExcerpt.Build([A("Kaç adet?"), U("3 adet")], " 3 adet ")).Should().Equal("assistant: Kaç adet?", "customer: 3 adet");
    }

    [Fact]
    public void TheSameShortAnswerEarlier_DoesNotHideTheNewOne()
    {
        Lines(ConversationExcerpt.Build([U("Evet"), A("Peki iade de açayım mı?")], "Evet"))
            .Should().Equal("customer: Evet", "assistant: Peki iade de açayım mı?", "customer: Evet");
    }

    [Fact]
    public void OnlyTheNewestMessages_AreShown()
    {
        var before = Enumerable.Range(1, 10).Select(i => U($"m{i}")).ToList();

        var lines = ConversationExcerpt.Build(before, "son");

        lines.Should().HaveCount(ConversationExcerpt.MaxMessages);
        lines[^1].Text.Should().Be("son");
        lines[0].Text.Should().Be("m6");
    }

    [Fact]
    public void VoicePlaceholders_AndEmptyMessages_AreLeftOut()
    {
        Lines(ConversationExcerpt.Build([U("(sesli)"), U("  "), A("Merhaba")], "(sesli)")).Should().Equal("assistant: Merhaba");
    }

    [Fact]
    public void Markdown_IsRemovedFromBotMessages()
    {
        ConversationExcerpt.Build([A("**1057** numaralı sipariş")], null).Single().Text.Should().Be("1057 numaralı sipariş");
    }

    [Fact]
    public void VeryLongMessages_AreShortened()
    {
        var text = ConversationExcerpt.Build([U(new string('x', 5000))], null).Single().Text;

        text.Length.Should().BeLessThanOrEqualTo(ConversationExcerpt.MaxTextLength);
        text.Should().EndWith("…");
    }

    [Fact]
    public void NothingSaved_StillShowsWhatTheApprovalRemembers()
    {
        Lines(ConversationExcerpt.Build([], "Bozuk ürün geldi")).Should().Equal("customer: Bozuk ürün geldi");
    }

    [Fact]
    public void NothingAtAll_IsEmpty()
    {
        ConversationExcerpt.Build([], "  ").Should().BeEmpty();
    }

    [Fact]
    public void AnsweredTurn_IsNotRepeatedAfterTheBotReply()
    {
        // Eskalasyon turun sonunda oluşur: cümle ve yanıtı çoğu zaman kayıttan önce yazılmıştır (ölçüldü: 8 ms).
        Lines(ConversationExcerpt.Build([U("Temsilci istiyorum"), A("Sizi aktarıyorum.")], "Temsilci istiyorum", turnMayBeAnswered: true))
            .Should().Equal("customer: Temsilci istiyorum", "assistant: Sizi aktarıyorum.");
    }

    [Fact]
    public void AnsweredTurn_StillAddsASentenceThatIsNotSavedYet()
    {
        // Sesli kanalda talep tool çağrısıyla açılır, tur daha yazılmamıştır.
        Lines(ConversationExcerpt.Build([U("Merhaba"), A("Buyurun")], "Temsilci istiyorum", turnMayBeAnswered: true))
            .Should().Equal("customer: Merhaba", "assistant: Buyurun", "customer: Temsilci istiyorum");
    }

    private static EscalationRequest Escalation(string id, string? session, string query, DateTime at) =>
        new() { Id = id, SessionId = session, UserQuery = query, CreatedAt = at };

    private static (EscalationPortService Service, IConversationExcerptStore Store) BuildEscalations()
    {
        var store = Substitute.For<IConversationExcerptStore>();
        return (new EscalationPortService(Substitute.For<IEscalationSink>(), NullLogger<EscalationPortService>.Instance, store), store);
    }

    [Fact]
    public async Task Escalations_ReadTheTurnWrittenAroundTheSameMoment()
    {
        var at = new DateTime(2026, 11, 1, 5, 0, 56, DateTimeKind.Utc);
        var (service, store) = BuildEscalations();
        store.GetBeforeAsync("s1", Arg.Is<DateTime>(d => d > at), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([U("olmadı yine"), A("Temsilciye aktarıyorum.")]);

        var map = await service.GetConversationsAsync([Escalation("e1", "s1", "olmadı yine", at)], TestContext.Current.CancellationToken);

        Lines(map["e1"]).Should().Equal("customer: olmadı yine", "assistant: Temsilciye aktarıyorum.");
    }

    [Fact]
    public async Task Escalations_DoNotReachIntoLaterConversation()
    {
        var at = new DateTime(2026, 11, 1, 5, 0, 56, DateTimeKind.Utc);
        var (service, store) = BuildEscalations();

        await service.GetConversationsAsync([Escalation("e1", "s1", "x", at)], TestContext.Current.CancellationToken);

        await store.Received(1).GetBeforeAsync(
            "s1", Arg.Is<DateTime>(d => d <= at.AddSeconds(10)), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Escalations_WithoutASession_ShowTheirOwnSentence()
    {
        var (service, store) = BuildEscalations();

        var map = await service.GetConversationsAsync([Escalation("e1", null, "Yardım", DateTime.UtcNow)], TestContext.Current.CancellationToken);

        Lines(map["e1"]).Should().Equal("customer: Yardım");
        await store.DidNotReceiveWithAnyArgs().GetBeforeAsync(default!, default, default, default);
    }

    private static (ApprovalPortService Service, IConversationExcerptStore Store) Build(ApprovalRequest? request)
    {
        var queue = Substitute.For<IApprovalQueue>();
        queue.GetAsync("a1", Arg.Any<CancellationToken>()).Returns(request);
        var store = Substitute.For<IConversationExcerptStore>();
        var service = new ApprovalPortService(
            queue, Substitute.For<ICustomerRepository>(), NullLogger<ApprovalPortService>.Instance, store);
        return (service, store);
    }

    [Fact]
    public async Task Port_ReadsOnlyMessagesFromBeforeTheRequest()
    {
        var at = new DateTime(2026, 11, 1, 18, 51, 58, DateTimeKind.Utc);
        var (service, store) = Build(new ApprovalRequest { SessionId = "s1", UserQuery = "Evet", RequestedAt = at });
        store.GetBeforeAsync("s1", at, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([U("3 adet"), A("Göndereyim mi?")]);

        var lines = await service.GetConversationAsync("a1", TestContext.Current.CancellationToken);

        Lines(lines!).Should().Equal("customer: 3 adet", "assistant: Göndereyim mi?", "customer: Evet");
    }

    [Fact]
    public async Task Port_UnknownApproval_IsNull()
    {
        var (service, _) = Build(null);

        (await service.GetConversationAsync("a1", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Port_ApprovalWithoutASession_ShowsItsOwnSentence()
    {
        var (service, store) = Build(new ApprovalRequest { SessionId = null, UserQuery = "Bozuk ürün geldi" });

        var lines = await service.GetConversationAsync("a1", TestContext.Current.CancellationToken);

        Lines(lines!).Should().Equal("customer: Bozuk ürün geldi");
        await store.DidNotReceiveWithAnyArgs().GetBeforeAsync(default!, default, default, default);
    }
}
