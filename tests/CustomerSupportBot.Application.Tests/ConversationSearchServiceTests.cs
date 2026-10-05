// Konuşma arama: doğrulama, depoya giden ölçütler, sayfalama, vurgulu alıntı.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Conversations;

namespace CustomerSupportBot.Application.Tests;

public class ConversationSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static (ConversationSearchService Service, IConversationSearchStore Store, List<ConversationSearchCriteria> Seen) Build(
        Func<ConversationSearchCriteria, IReadOnlyList<ConversationSearchRow>>? rows = null)
    {
        var seen = new List<ConversationSearchCriteria>();
        var store = Substitute.For<IConversationSearchStore>();
        store.SearchAsync(Arg.Any<ConversationSearchCriteria>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var c = ci.Arg<ConversationSearchCriteria>();
            seen.Add(c);
            return rows?.Invoke(c) ?? [];
        });
        return (new ConversationSearchService(store), store, seen);
    }

    private static ConversationSearchRow Row(string sid, string? matched = null) =>
        new(sid, "1001", T0, T0, 4, matched, ["resolved"], ["kargo"]);

    [Fact]
    public async Task Criteria_AreNormalized_AndPageAsksForOneExtraRow()
    {
        var (service, _, seen) = Build();

        var r = await service.SearchAsync(new ConversationSearchQuery(
            Text: "  İADE ", CustomerId: " 1001 ", From: T0.AddDays(-7), To: T0, Reason: " resolved ", Tag: " Kargo Gecikmesi ", Page: 3), Ct);

        r.Error.Should().BeNull();
        var c = seen.Single();
        c.FoldedText.Should().Be("iade");
        c.CustomerId.Should().Be("1001");
        c.ReasonCode.Should().Be("resolved");
        c.Tag.Should().Be("kargo-gecikmesi");
        c.FromUtc.Should().Be(T0.AddDays(-7));
        c.ToUtc.Should().Be(T0);
        c.Offset.Should().Be(2 * ConversationSearchService.PageSize);
        c.Take.Should().Be(ConversationSearchService.PageSize + 1);
    }

    [Fact]
    public async Task NoFilters_ListsRecentConversations()
    {
        var (service, _, seen) = Build();

        (await service.SearchAsync(new ConversationSearchQuery(null, null, null, null, null, null), Ct)).Error.Should().BeNull();

        seen.Single().Should().Be(new ConversationSearchCriteria(null, null, null, null, null, null, 0, ConversationSearchService.PageSize + 1));
    }

    [Theory]
    [InlineData("a", null, "en az 2")]
    [InlineData(null, -1, "Başlangıç")]   // From > To
    public async Task InvalidQuery_IsRejected_WithoutHittingTheStore(string? text, int? fromOffsetDays, string error)
    {
        var (service, store, _) = Build();
        var from = fromOffsetDays is null ? (DateTime?)null : T0.AddDays(1);

        var r = await service.SearchAsync(new ConversationSearchQuery(text, null, from, fromOffsetDays is null ? null : T0, null, null), Ct);

        r.Error.Should().Contain(error);
        await store.DidNotReceiveWithAnyArgs().SearchAsync(default!, Ct);
    }

    [Fact]
    public async Task TooLongText_IsRejected()
    {
        var (service, _, _) = Build();

        (await service.SearchAsync(new ConversationSearchQuery(new string('x', 201), null, null, null, null, null), Ct))
            .Error.Should().Contain("en fazla 200");
    }

    [Fact]
    public async Task HasMore_WhenTheStoreReturnsTheExtraRow()
    {
        var (service, _, _) = Build(c => Enumerable.Range(0, c.Take).Select(i => Row($"s{i}")).ToList());

        var r = await service.SearchAsync(new ConversationSearchQuery(null, null, null, null, null, null), Ct);

        r.Page!.Items.Should().HaveCount(ConversationSearchService.PageSize);
        r.Page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Snippet_IsCutAroundTheMatch_AndMarksIt_DespiteTurkishCaseDifferences()
    {
        var text = new string('a', 100) + " siparişimin İADESİ ne zaman yapılır " + new string('b', 100);
        var (service, _, _) = Build(_ => [Row("s1", text)]);

        var hit = (await service.SearchAsync(new ConversationSearchQuery("iadesi", null, null, null, null, null), Ct)).Page!.Items.Single();

        hit.Snippet.Should().StartWith("…").And.EndWith("…");
        hit.Snippet!.Length.Should().BeLessThanOrEqualTo(ConversationSearchService.SnippetLength + 2);
        hit.Snippet.Substring(hit.HighlightStart!.Value, hit.HighlightLength!.Value).Should().Be("İADESİ");
        hit.Reasons.Should().Equal("resolved");
        hit.Tags.Should().Equal("kargo");
    }

    [Fact]
    public async Task WithoutText_SnippetIsTheFirstCustomerMessage_Unmarked()
    {
        var (service, _, _) = Build(_ => [Row("s1", "Kargom nerede?")]);

        var hit = (await service.SearchAsync(new ConversationSearchQuery(null, "1001", null, null, null, null), Ct)).Page!.Items.Single();

        hit.Snippet.Should().Be("Kargom nerede?");
        hit.HighlightStart.Should().BeNull();
    }

    [Theory]
    [InlineData("İADE Iİı ÇĞÖŞÜ Kargo", "iade iii çğöşü kargo")]
    [InlineData("", "")]
    public void Fold_IsTurkishAware_AndKeepsTheLength(string input, string expected)
    {
        TurkishText.Fold(input).Should().Be(expected).And.HaveLength(input.Length);
    }
}
